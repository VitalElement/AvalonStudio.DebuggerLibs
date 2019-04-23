using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Samples.Debugging.CorDebug;
using Microsoft.Samples.Debugging.CorDebug.NativeApi;
using Microsoft.Samples.Debugging.CorMetadata;
using Microsoft.Samples.Debugging.CorSymbolStore;
using Microsoft.Samples.Debugging.Extensions;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation;
using System.Linq;
using ISymbolReader = System.Diagnostics.SymbolStore.ISymbolReader;

namespace Mono.Debugging.Win32
{
	public class CorDebuggerSession: DebuggerSession
	{
		protected bool _attachMode = false;
		readonly char[] badPathChars;
		readonly object debugLock = new object ();
		readonly object terminateLock = new object ();

		protected CorDebugger dbg;
		protected CorApi.Portable.Process process;
		CorApi.Portable.Thread activeThread;
		CorApi.Portable.Stepper stepper;
		bool terminated;
		bool evaluating;
		bool autoStepInto;
		bool stepInsideDebuggerHidden=false;
		protected uint processId;
		protected bool attaching = false;

		static int evaluationTimestamp;

		readonly SymbolBinder symbolBinder = new SymbolBinder ();
		readonly object appDomainsLock = new object ();

		Dictionary<uint, AppDomainInfo> appDomains = new Dictionary<uint, AppDomainInfo> ();
		Dictionary<uint, ProcessInfo> processes = new Dictionary<uint, ProcessInfo> ();
		Dictionary<uint, ThreadInfo> threads = new Dictionary<uint,ThreadInfo> ();
		readonly Dictionary<CorApi.Portable.Breakpoint, BreakEventInfo> breakpoints = new Dictionary<CorApi.Portable.Breakpoint, BreakEventInfo> ();
		readonly Dictionary<ulong, CorApi.Portable.HandleValue> handles = new Dictionary<ulong, CorApi.Portable.HandleValue>();

		readonly BlockingCollection<Action> helperOperationsQueue = new BlockingCollection<Action>(new ConcurrentQueue<Action>());
		readonly CancellationTokenSource helperOperationsCancellationTokenSource = new CancellationTokenSource ();

		public CorObjectAdaptor ObjectAdapter = new CorObjectAdaptor();

		class AppDomainInfo
		{
			public CorApi.Portable.AppDomain AppDomain;
			public Dictionary<string, DocInfo> Documents;
			public Dictionary<string, ModuleInfo> Modules;
		}

		class DocInfo
		{
			public ISymbolDocument Document;
			public ModuleInfo ModuleInfo;
		}

		class ModuleInfo
		{
			public ISymbolReader Reader;
			public CorApi.Portable.Module Module;
			public CorMetadataImport Importer;
		}

		public CorDebuggerSession(char[] badPathChars)
		{	
			this.badPathChars = badPathChars;
			ObjectAdapter.BusyStateChanged += (sender, e) => SetBusyState (e);
			var cancellationToken = helperOperationsCancellationTokenSource.Token;
			new Thread (() => {
				try {
					while (!cancellationToken.IsCancellationRequested) {
						var action = helperOperationsQueue.Take(cancellationToken);
						try {
							action ();
						}
						catch (Exception e) {
							DebuggerLoggingService.LogError ("Exception on processing helper thread action", e);
						}
					}

				}
				catch (Exception e) {
					if (e is OperationCanceledException || e is ObjectDisposedException) {
						DebuggerLoggingService.LogMessage ("Helper thread was gracefully interrupted");
					}
					else {
						DebuggerLoggingService.LogError ("Unhandled exception in helper thread. Helper thread is terminated", e);
					}
				}
			}) {Name = "CorDebug helper thread "}.Start();
		}

		public new IDebuggerSessionFrontend Frontend {
			get { return base.Frontend; }
		}

		public static int EvaluationTimestamp {
			get { return evaluationTimestamp; }
		}

		public ICustomCorSymbolReaderFactory CustomSymbolReaderFactory { get; set; }

		internal CorApi.Portable.Process Process
		{
			get
			{
				return process;
			}
		}

		public override void Dispose ( )
		{
			MtaThread.Run (delegate
			{
				TerminateDebugger ();
				ObjectAdapter.Dispose();
			});
			helperOperationsCancellationTokenSource.Dispose ();
			helperOperationsQueue.Dispose ();

			base.Dispose ();

			// There is no explicit way of disposing the metadata objects, so we have
			// to rely on the GC to do it.

			lock (appDomainsLock) {
				foreach (var appDomainInfo in appDomains) {
					foreach (var module in appDomainInfo.Value.Modules.Values) {
						var disposable = module.Reader as IDisposable;
						if (disposable != null)
							disposable.Dispose ();
					}
				}
				appDomains = null;
			}

			threads = null;
			processes = null;
			activeThread = null;

			ThreadPool.QueueUserWorkItem (delegate {
				Thread.Sleep (2000);
				GC.Collect ();
				GC.WaitForPendingFinalizers ();
				Thread.Sleep (20000);
				GC.Collect ();
				GC.WaitForPendingFinalizers ();
			});
		}

		void QueueToHelperThread (Action action)
		{
			helperOperationsQueue.Add (action);
		}

		void DeactivateBreakpoints ()
		{
			var breakpointsCopy = breakpoints.Keys.ToList ();
			foreach (var corBreakpoint in breakpointsCopy) {
				try {
					corBreakpoint.Activate (false);
				}
				catch (Exception e) {
					DebuggerLoggingService.LogMessage ("Exception in DeactivateBreakpoints(): {0}", e);
				}
			}
		}

		void TerminateDebugger ()
		{
			helperOperationsCancellationTokenSource.Cancel();
			DeactivateBreakpoints ();
			lock (terminateLock) {
				if (terminated)
					return;

				terminated = true;

				if (process != null) {
					// Process already running. Stop it. In the ProcessExited event the
					// debugger engine will be terminated
					try {
						process.Stop (0);
						if (attaching) {
							process.Detach ();
						}
						else {
							process.Terminate (1);
						}
					}
					catch (SharpGen.Runtime.SharpGenException e) {
						// process was terminated, but debugger operation thread doesn't call ProcessExit callback at the time,
						// so we just think that the process is alive but that's wrong.
						// This may happen when e.g. when target process exited and Dispose was called at the same time
						// rethrow the exception in other case
						if (e.HResult != (int) HResult.CORDBG_E_PROCESS_TERMINATED) {
							throw;
						}
					}
				}
			}
		}

		protected override void OnRun (DebuggerStartInfo startInfo)
		{
			MtaThread.Run (delegate
			{
				var env = PrepareEnvironment (startInfo);
				var cmdLine = PrepareCommandLine (startInfo);
				var dir = PrepareWorkingDirectory (startInfo);
				int flags = 0;
				if (!startInfo.UseExternalConsole) {
					flags = (int)CreationFlags.CREATE_NO_WINDOW;
						flags |= DebuggerExtensions.CREATE_REDIRECT_STD;
				}

				// Create the debugger

				string dversion;
				try {
					dversion = CorDebugger.GetDebuggerVersionFromFile (startInfo.Command);
				}
				catch {
					dversion = CorDebugger.GetDefaultDebuggerVersion ();
				}
				dbg = new CorDebugger (dversion);
				throw new NotImplementedException();
				//process = dbg.CreateProcess (startInfo.Command, cmdLine, dir, env, flags);
				processId = process.Id;
				
				//SetupProcess (process);
				process.Continue (false);
			});
			OnStarted ();
		}

		protected static string PrepareWorkingDirectory (DebuggerStartInfo startInfo)
		{
			var dir = startInfo.WorkingDirectory;
			if (string.IsNullOrEmpty (dir))
				dir = Path.GetDirectoryName (startInfo.Command);
			return dir;
		}

		protected static string PrepareCommandLine (DebuggerStartInfo startInfo)
		{
			// The second parameter of CreateProcess is the command line, and it includes the application being launched
			string cmdLine = "\"" + startInfo.Command + "\" " + startInfo.Arguments;
			return cmdLine;
		}

		protected static Dictionary<string, string> PrepareEnvironment (DebuggerStartInfo startInfo)
		{
			Dictionary<string, string> env = new Dictionary<string, string> ();
			foreach (DictionaryEntry de in Environment.GetEnvironmentVariables ())
				env[(string) de.Key] = (string) de.Value;

			foreach (KeyValuePair<string, string> var in startInfo.EnvironmentVariables)
				env[var.Key] = var.Value;
			return env;
		}

		protected void SetupProcess (CorApi.Portable.Process corProcess)
		{
			processId = corProcess.Id;
			corProcess.OnCreateProcess += OnCreateProcess;
			corProcess.OnCreateAppDomain += OnCreateAppDomain;
			corProcess.OnAppDomainExit += OnAppDomainExit;
			corProcess.OnAssemblyLoad += OnAssemblyLoad;
			corProcess.OnAssemblyUnload += OnAssemblyUnload;
			corProcess.OnCreateThread += OnCreateThread;
			corProcess.OnThreadExit += OnThreadExit;
			corProcess.OnModuleLoad += OnModuleLoad;
			corProcess.OnModuleUnload += OnModuleUnload;
			corProcess.OnProcessExit += OnProcessExit;
			corProcess.OnUpdateModuleSymbols += OnUpdateModuleSymbols;
			corProcess.OnDebuggerError += OnDebuggerError;
			corProcess.OnBreakpoint += OnBreakpoint;
			corProcess.OnStepComplete += OnStepComplete;
			corProcess.OnBreak += OnBreak;
			corProcess.OnNameChange += OnNameChange;
			corProcess.OnEvalComplete += OnEvalComplete;
			corProcess.OnEvalException += OnEvalException;
			corProcess.OnLogMessage += OnLogMessage;
			corProcess.OnException2 += OnException2;
		    CorApi.Portable.CorProcessExtensions.RegisterStdOutput(corProcess, OnStdOutput);
		}

		void OnStdOutput (object sender, CorApi.Portable.CorTargetOutputEventArgs e)
		{
			OnTargetOutput (e.IsStdError, e.Text);
		}

		void OnLogMessage (object sender, CorApi.Portable.LogMessageEventArgs e)
		{
			OnTargetDebug (e.Level, e.LogSwitchName, e.Message);
			e.Continue = true;
		}

		void OnEvalException (object sender, CorApi.Portable.EvalEventArgs e)
		{
			evaluationTimestamp++;
		}

		void OnEvalComplete (object sender, CorApi.Portable.EvalEventArgs e)
		{
			evaluationTimestamp++;
		}

		void OnNameChange (object sender, CorApi.Portable.ThreadEventArgs e)
		{
		}

		void OnStopped ( )
		{
			evaluationTimestamp++;
			lock (threads) {
				threads.Clear ();
			}
		}

		void OnBreak (object sender, CorApi.Portable.ThreadEventArgs e)
		{
			lock (debugLock) {
				if (evaluating) {
					e.Continue = true;
					return;
				}
			}
			OnStopped ();
			e.Continue = false;
			SetActiveThread (e.Thread);
			TargetEventArgs args = new TargetEventArgs (TargetEventType.TargetInterrupted);
			args.Process = GetProcess (process);
			args.Thread = GetThread (e.Thread);
			args.Backtrace = new Backtrace (new CorBacktrace (e.Thread, this));
			OnTargetEvent (args);
		}

		bool StepThrough (MethodInfo methodInfo)
		{
			var m = methodInfo.GetCustomAttributes (true);
			if (Options.ProjectAssembliesOnly) {
				return methodInfo.GetCustomAttributes (true).Union (methodInfo.DeclaringType.GetCustomAttributes (true)).Any (v =>
					v is System.Diagnostics.DebuggerHiddenAttribute ||
					v is System.Diagnostics.DebuggerStepThroughAttribute ||
					v is System.Diagnostics.DebuggerNonUserCodeAttribute);
			} else {
				return methodInfo.GetCustomAttributes (true).Union (methodInfo.DeclaringType.GetCustomAttributes (true)).Any (v =>
					v is System.Diagnostics.DebuggerHiddenAttribute ||
					v is System.Diagnostics.DebuggerStepThroughAttribute);
			}
		}

		bool ContinueOnStepIn(MethodInfo methodInfo)
		{
			return methodInfo.GetCustomAttributes (true).Any (v => v is System.Diagnostics.DebuggerStepperBoundaryAttribute);
		}

		static bool IsPropertyOrOperatorMethod (MethodInfo method)
		{
			if (method == null)
				return false;
			string name = method.Name;

			return method.IsSpecialName &&
			(name.StartsWith ("get_", StringComparison.Ordinal) ||
			name.StartsWith ("set_", StringComparison.Ordinal) ||
			name.StartsWith ("op_", StringComparison.Ordinal));
		}

		static bool IsCompilerGenerated (MethodInfo method)
		{
			return method.GetCustomAttributes (true).Any (v => v is System.Runtime.CompilerServices.CompilerGeneratedAttribute);
		}

		void OnStepComplete (object sender, CorApi.Portable.StepCompleteEventArgs e)
		{
			lock (debugLock) {
				if (evaluating) {
					e.Continue = true;
					return;
				}
			}

			bool localAutoStepInto = autoStepInto;
			autoStepInto = false;
			bool localStepInsideDebuggerHidden = stepInsideDebuggerHidden;
			stepInsideDebuggerHidden = false;

			if (e.AppDomain.Process.HasQueuedCallbacks (e.Thread)) {
				e.Continue = true;
				return;
			}

			if (localAutoStepInto) {
				Step (true);
				e.Continue = true;
				return;
			}

			if (ContinueOnStepIn (e.Thread.ActiveFrame.Function.GetMethodInfo (this))) {
				e.Continue = true;
				return;
			}

			var currentSequence = CorBacktrace.GetSequencePoint (this, e.Thread.ActiveFrame);
			if (currentSequence == null) {
				stepper.Step (true);
				autoStepInto = true;
				e.Continue = true;
				return;
			}

			if (StepThrough (e.Thread.ActiveFrame.Function.GetMethodInfo (this))) {
				stepInsideDebuggerHidden = e.StepReason == CorApi.Portable.CorDebugStepReason.StepCall;
				RawContinue (true, true);
				e.Continue = true;
				return;
			}

			if ((Options.StepOverPropertiesAndOperators || IsCompilerGenerated(e.Thread.ActiveFrame.Function.GetMethodInfo (this))) &&
			    IsPropertyOrOperatorMethod (e.Thread.ActiveFrame.Function.GetMethodInfo (this)) &&
				e.StepReason == CorApi.Portable.CorDebugStepReason.StepCall) {
				stepper.StepOut ();
				autoStepInto = true;
				e.Continue = true;
				return;
			}

			if (currentSequence.IsSpecial) {
				Step (false);
				e.Continue = true;
				return;
			}

			if (localStepInsideDebuggerHidden && e.StepReason == CorApi.Portable.CorDebugStepReason.StepReturn) {
				Step (true);
				e.Continue = true;
				return;
			}

			OnStopped ();
			e.Continue = false;
			SetActiveThread (e.Thread);
			TargetEventArgs args = new TargetEventArgs (TargetEventType.TargetStopped);
			args.Process = GetProcess (process);
			args.Thread = GetThread (e.Thread);
			args.Backtrace = new Backtrace (new CorBacktrace (e.Thread, this));
			OnTargetEvent (args);
		}

		void OnThreadExit (object sender, CorApi.Portable.ThreadEventArgs e)
		{
			lock (threads) {
				threads.Remove (e.Thread.Id);
			}
		}

		void OnBreakpoint (object sender, CorApi.Portable.BreakpointEventArgs e)
		{
			lock (debugLock) {
				if (evaluating) {
					e.Continue = true;
					return;
				}
			}

			// we have to stop an execution and enqueue breakpoint calculations on another thread to release debugger event thread for further events
			// we can't perform any evaluations inside this handler, because the debugger thread is busy and we won't get evaluation events there
			e.Continue = false;

			QueueToHelperThread (() => {
				BreakEventInfo binfo;
				BreakEvent breakEvent = null;
				if (e.Controller.IsRunning)
					throw new InvalidOperationException ("Debuggee isn't stopped to perform breakpoint calculations");

				var shouldContinue = false;
				if (breakpoints.TryGetValue (e.Breakpoint, out binfo)) {
					breakEvent = (Breakpoint) binfo.BreakEvent;
					try {
						shouldContinue = ShouldContinueOnBreakpoint (e.Thread, binfo);
					}
					catch (Exception ex) {
						DebuggerLoggingService.LogError ("ShouldContinueOnBreakpoint() has thrown an exception", ex);
					}
				}

				if (shouldContinue || e.AppDomain.Process.HasQueuedCallbacks (e.Thread)) {
					e.Controller.SetAllThreadsDebugState (CorApi.Portable.CorDebugThreadState.ThreadRun, null);
					e.Controller.Continue (false);
					return;
				}

				OnStopped ();
				// If a breakpoint is hit while stepping, cancel the stepping operation
				if (stepper != null && stepper.IsActive)
					stepper.Deactivate ();
				autoStepInto = false;
				SetActiveThread (e.Thread);
				var args = new TargetEventArgs (TargetEventType.TargetHitBreakpoint) {
					Process = GetProcess (process),
					Thread = GetThread (e.Thread),
					Backtrace = new Backtrace (new CorBacktrace (e.Thread, this)),
					BreakEvent = breakEvent
				};
				OnTargetEvent (args);
			});
		}

		bool ShouldContinueOnBreakpoint (CorApi.Portable.Thread thread, BreakEventInfo binfo)
		{
			var bp = (Breakpoint) binfo.BreakEvent;
			binfo.IncrementHitCount();
			if (!binfo.HitCountReached)
				return true;

			if (!string.IsNullOrEmpty (bp.ConditionExpression)) {
				try {
					string res = EvaluateExpression (thread, bp.ConditionExpression);
					if (bp.BreakIfConditionChanges) {
						if (res == bp.LastConditionValue)
							return true;
						bp.LastConditionValue = res;
					}
					else {
						if (res != null && res.ToLower () != "true")
							return true;
					}
				}
				catch (EvaluatorException e) {
					OnDebuggerOutput (false, e.Message);
					binfo.SetStatus (BreakEventStatus.Invalid, e.Message);
					return true;
				}
			}

			if ((bp.HitAction & HitAction.CustomAction) != HitAction.None) {
				// If custom action returns true, execution must continue
				if (binfo.RunCustomBreakpointAction (bp.CustomActionId))
					return true;
			}

			if ((bp.HitAction & HitAction.PrintTrace) != HitAction.None) {
				OnTargetDebug (0, "", "Breakpoint reached: " + bp.FileName + ":" + bp.Line + Environment.NewLine);
			}

			if ((bp.HitAction & HitAction.PrintExpression) != HitAction.None) {
				string exp = EvaluateTrace (thread, bp.TraceExpression);
				binfo.UpdateLastTraceValue (exp);
			}

			return (bp.HitAction & HitAction.Break) == HitAction.None;
		}

		void OnDebuggerError (object sender, CorApi.Portable.DebuggerErrorEventArgs e)
		{
			Exception ex = Marshal.GetExceptionForHR (e.HResult);
			OnDebuggerOutput (true, string.Format ("Debugger Error: {0}\n", ex.Message));
		}

		void OnUpdateModuleSymbols (object sender, CorApi.Portable.UpdateModuleSymbolsEventArgs e)
		{
			e.Continue = true;
		}

		void OnProcessExit (object sender, CorApi.Portable.ProcessEventArgs e)
		{
			TargetEventArgs args = new TargetEventArgs (TargetEventType.TargetExited);

			// If the main thread stopped, terminate the debugger session
			if (e.Process.Id == process.Id) {
				lock (terminateLock) {
					process.Dispose ();
					process = null;
					ThreadPool.QueueUserWorkItem (delegate
					{
						// The Terminate call will fail if called in the event handler
						dbg.Terminate ();
						dbg = null;
						GC.Collect ();
					});
				}
			}

			OnTargetEvent (args);
		}

		void OnAssemblyUnload (object sender, CorApi.Portable.AssemblyEventArgs e)
		{
			OnDebuggerOutput (false, string.Format ("Unloaded Module '{0}'\n", e.Assembly.Name));
			e.Continue = true;
		}

		void OnModuleLoad (object sender, CorApi.Portable.ModuleEventArgs e)
		{
			var currentModule = e.Module;
			using (var mi = new CorMetadataImport (currentModule)) {
				if (_attachMode) {
					try {
						// Required to avoid the jit to get rid of variables too early
						currentModule.JITCompilerFlags = CorApi.Portable.CorDebugJITCompilerFlags.CordebugJitDisableOptimization;
					} catch {
						// Some kind of modules don't allow JIT flags to be changed.
					}
				}

				var currentDomain = e.AppDomain;
				OnDebuggerOutput (false, String.Format ("Loading module {0} in application domain {1}:{2}\n", currentModule.Name, currentDomain.Id, currentDomain.Name));
				string file = currentModule.Assembly.Name;
				var newDocuments = new Dictionary<string, DocInfo> ();
				var justMyCode = false;
				ISymbolReader reader = null;
				if (file.IndexOfAny (badPathChars) == -1) {
					try {
						reader = symbolBinder.GetReaderForFile (mi.RawCOMObject, file, ".",
							SymSearchPolicies.AllowOriginalPathAccess | SymSearchPolicies.AllowReferencePathAccess);

						if (reader == null && CustomSymbolReaderFactory != null) {
							reader = CustomSymbolReaderFactory.CreateCustomSymbolReader (file);
						}

						if (reader != null) {
							OnDebuggerOutput (false, string.Format ("Symbols for module {0} loaded\n", file));
							// set JMC to true only when we got the reader.
							// When module JMC is true, debugger will step into it
							justMyCode = true;
							foreach (ISymbolDocument doc in reader.GetDocuments ()) {
								if (string.IsNullOrEmpty (doc.URL))
									continue;
								string docFile = System.IO.Path.GetFullPath (doc.URL);
								DocInfo di = new DocInfo ();
								di.Document = doc;
								newDocuments[docFile] = di;
							}
						}
					} catch (SharpGen.Runtime.SharpGenException ex) {
						var hResult = ex.ToHResult<PdbHResult> ();
						if (hResult != null) {
							if (hResult != PdbHResult.E_PDB_OK) {
								OnDebuggerOutput (false, string.Format ("Failed to load pdb for assembly {0}. Error code {1}(0x{2:X})\n", file, hResult, ex.HResult));
							}
						} else {
							DebuggerLoggingService.LogError (string.Format ("Loading symbols of module {0} failed", e.Module.Name), ex);
						}
					} catch (Exception ex) {
						DebuggerLoggingService.LogError (string.Format ("Loading symbols of module {0} failed", e.Module.Name), ex);
					}
				}
				try {
					currentModule.SetJMCStatus (justMyCode, null);
				} catch (SharpGen.Runtime.SharpGenException ex) {
					// somewhen exceptions is thrown
					DebuggerLoggingService.LogMessage ("Exception during setting JMC: {0}", ex.Message);
				}

				lock (appDomainsLock) {
					AppDomainInfo appDomainInfo;
					if (!appDomains.TryGetValue (currentDomain.Id, out appDomainInfo)) {
						DebuggerLoggingService.LogMessage ("OnCreatedAppDomain was not fired for domain {0} (id {1})", currentDomain.Name, currentDomain.Id);
						appDomainInfo = new AppDomainInfo {
							AppDomain = currentDomain,
							Documents = new Dictionary<string, DocInfo> (StringComparer.InvariantCultureIgnoreCase),
							Modules = new Dictionary<string, ModuleInfo> (StringComparer.InvariantCultureIgnoreCase)
						};
						appDomains[currentDomain.Id] = appDomainInfo;
					}
					var modules = appDomainInfo.Modules;
					if (modules.ContainsKey (currentModule.Name)) {
						DebuggerLoggingService.LogMessage ("Module {0} was already added for app domain {1} (id {2}). Replacing\n",
							  currentModule.Name, currentDomain.Name, currentDomain.Id);
					}
					var newModuleInfo = new ModuleInfo {
						Module = currentModule,
						Reader = reader,
						Importer = mi,
					};
					modules[currentModule.Name] = newModuleInfo;
					var existingDocuments = appDomainInfo.Documents;
					foreach (var newDocument in newDocuments) {
						var documentFile = newDocument.Key;
						var newDocInfo = newDocument.Value;
						if (existingDocuments.ContainsKey (documentFile)) {
							DebuggerLoggingService.LogMessage ("Document {0} was already added for module {1} in domain {2} (id {3}). Replacing\n",
								  documentFile, currentModule.Name, currentDomain.Name, currentDomain.Id);
						}
						newDocInfo.ModuleInfo = newModuleInfo;
						existingDocuments[documentFile] = newDocInfo;
					}

				}

				foreach (var newFile in newDocuments.Keys) {
					BindSourceFileBreakpoints (newFile);
				}

				e.Continue = true;
			}
		}

		void OnModuleUnload (object sender, CorApi.Portable.ModuleEventArgs e)
		{
			var currentDomain = e.AppDomain;
			var currentModule = e.Module;
			var documentsToRemove = new List<string> ();
			lock (appDomainsLock) {
				AppDomainInfo appDomainInfo;
				if (!appDomains.TryGetValue (currentDomain.Id, out appDomainInfo)) {
				  DebuggerLoggingService.LogMessage ("Failed unload module {0} for app domain {1} (id {2}) because app domain was not found or already unloaded\n",
							currentModule.Name, currentDomain.Name, currentDomain.Id);
					return;
				}
				ModuleInfo moi;
				if (!appDomainInfo.Modules.TryGetValue (currentModule.Name, out moi)) {
				  DebuggerLoggingService.LogMessage ("Failed unload module {0} for app domain {1} (id {2}) because the module was not found or already unloaded\n",
						currentModule.Name, currentDomain.Name, currentDomain.Id);
				}
				else {
					appDomainInfo.Modules.Remove (currentModule.Name);
					var disposableReader = moi.Reader as IDisposable;
					if (disposableReader != null)
						disposableReader.Dispose ();
				}

				foreach (var docInfo in appDomainInfo.Documents) {
					if (docInfo.Value.ModuleInfo.Module.Name == currentModule.Name)
						documentsToRemove.Add (docInfo.Key);
				}
				foreach (var file in documentsToRemove) {
					appDomainInfo.Documents.Remove (file);
				}
			}
			foreach (var file in documentsToRemove) {
				UnbindSourceFileBreakpoints (file);
			}
		}

		void OnCreateAppDomain (object sender, CorApi.Portable.AppDomainEventArgs e)
		{
			var appDomainId = e.AppDomain.Id;
			lock (appDomainsLock) {
				if (!appDomains.ContainsKey (appDomainId)) {
					appDomains[appDomainId] = new AppDomainInfo {
						AppDomain = e.AppDomain,
						Documents = new Dictionary<string, DocInfo> (StringComparer.InvariantCultureIgnoreCase),
						Modules = new Dictionary<string, ModuleInfo> (StringComparer.InvariantCultureIgnoreCase)
					};
				}
				else {
					DebuggerLoggingService.LogMessage ("App domain {0} (id {1}) was already loaded", e.AppDomain.Name, appDomainId);
				}
			}
			e.AppDomain.Attach();
			e.Continue = true;
			OnDebuggerOutput (false, string.Format("Loaded application domain '{0} (id {1})'\n", e.AppDomain.Name, appDomainId));
		}

		private void OnAppDomainExit (object sender, CorApi.Portable.AppDomainEventArgs e)
		{
			var appDomainId = e.AppDomain.Id;
			lock (appDomainsLock) {
				if (!appDomains.Remove (appDomainId)) {
				  DebuggerLoggingService.LogMessage ("Failed to unload app domain {0} (id {1}) because it's not found in map. Possibly already unloaded.", e.AppDomain.Name, appDomainId);
				}
			}
			// Detach is not implemented for ICorDebugAppDomain, it's valid only for ICorDebugProcess
			//e.AppDomain.Detach ();
			e.Continue = true;
			OnDebuggerOutput (false, string.Format("Unloaded application domain '{0} (id {1})'\n", e.AppDomain.Name, appDomainId));
		}


		void OnCreateProcess (object sender, CorApi.Portable.ProcessEventArgs e)
		{
			// Required to avoid the jit to get rid of variables too early
			if (_attachMode)
			{
				try
				{
					e.Process.DesiredNGENCompilerFlags = CorApi.Portable.CorDebugJITCompilerFlags.CordebugJitDisableOptimization;
				}
				catch (Exception ex)
				{
					DebuggerLoggingService.LogMessage(string.Format("Unable to set e.Process.DesiredNGENCompilerFlags, possibly because the process was attached: {0}", ex.Message));
				}
			}

			e.Process.EnableLogMessages (true);
			e.Continue = true;
		}
		void OnCreateThread (object sender, CorApi.Portable.ThreadEventArgs e)
		{
			OnDebuggerOutput (false, string.Format ("Started Thread {0}\n", e.Thread.Id));
			e.Continue = true;
		}

		void OnAssemblyLoad (object sender, CorApi.Portable.AssemblyEventArgs e)
		{
			OnDebuggerOutput (false, string.Format ("Loaded Assembly '{0}'\n", e.Assembly.Name));
			e.Continue = true;
		}
		
		void OnException2 (object sender, CorApi.Portable.Exception2EventArgs e)
		{
			lock (debugLock) {
				if (evaluating) {
					e.Continue = true;
					return;
				}
			}
			
			TargetEventArgs args = null;
			
			switch (e.EventType) {
				case CorApi.Portable.CorDebugExceptionCallbackType.DebugExceptionFirstChance:
					if (!this.Options.ProjectAssembliesOnly && IsCatchpoint (e))
						args = new TargetEventArgs (TargetEventType.ExceptionThrown);
					break;
				case CorApi.Portable.CorDebugExceptionCallbackType.DebugExceptionUserFirstChance:
					if (IsCatchpoint (e))
						args = new TargetEventArgs (TargetEventType.ExceptionThrown);
					break;
				case CorApi.Portable.CorDebugExceptionCallbackType.DebugExceptionCatchHandlerFound:
					break;
				case CorApi.Portable.CorDebugExceptionCallbackType.DebugExceptionUnhandled:
					args = new TargetEventArgs (TargetEventType.UnhandledException);
					break;
			}
			
			if (args != null) {
				OnStopped ();
				e.Continue = false;
				// If an exception is thrown while stepping, cancel the stepping operation
				if (stepper != null && stepper.IsActive)
					stepper.Deactivate ();
				autoStepInto = false;
				SetActiveThread (e.Thread);
				
				args.Process = GetProcess (process);
				args.Thread = GetThread (e.Thread);
				args.Backtrace = new Backtrace (new CorBacktrace (e.Thread, this));
				OnTargetEvent (args);	
			}
		}

		public bool IsExternalCode (string fileName)
		{
			if (string.IsNullOrWhiteSpace (fileName))
				return true;
			lock (appDomainsLock) {
				foreach (var appDomainInfo in appDomains) {
					if (appDomainInfo.Value.Documents.ContainsKey (fileName))
						return false;
				}
			}
			return true;
		}

		private bool IsCatchpoint (CorApi.Portable.Exception2EventArgs e)
		{
			// Build up the exception type hierachy
			CorApi.Portable.Value v = e.Thread.CurrentException;
			List<string> exceptions = new List<string>();
			CorApi.Portable.Type t = v.ExactType;
			while (t != null) {
				exceptions.Add(t.GetTypeInfo(this).FullName);
				t = t.Base;
			}
			if (exceptions.Count == 0)
				return false;
			// See if a catchpoint is set for this exception.
			foreach (Catchpoint cp in Breakpoints.GetCatchpoints()) {
				if (cp.Enabled &&
				    ((cp.IncludeSubclasses && exceptions.Contains (cp.ExceptionName)) ||
				    (exceptions [0] == cp.ExceptionName))) {
					return true;
				}
			}
			
			return false;
		}

		protected override void OnAttachToProcess(long procId)
		{
			attaching = true;
			MtaThread.Run(delegate
			{
				var version = CorDebugger.GetProcessLoadedRuntimes((int)procId);
				if (!version.Any())
					throw new InvalidOperationException(string.Format("Process {0} doesn't have .NET loaded runtimes", procId));
				dbg = new CorDebugger(version.Last());
				var lprocess = dbg.DebugActiveProcess((int)procId, false);
				lprocess.Continue(new SharpGen.Runtime.Win32.RawBool(false));

				throw new NotImplementedException();
				//SetupProcess(process);
				//process.Continue(false);
			});
			OnStarted();
		}

		protected override void OnAttachToProcess(ProcessInfo processInfo)
		{
			var clrProcessInfo = processInfo as ClrProcessInfo;
			var version = clrProcessInfo != null ? clrProcessInfo.Runtime : null;

			attaching = true;
			MtaThread.Run(delegate
			{
				var versions = CorDebugger.GetProcessLoadedRuntimes((int)processInfo.Id);
				if (!versions.Any())
					throw new InvalidOperationException(string.Format("Process {0} doesn't have .NET loaded runtimes", processInfo.Id));

				if (version == null || !versions.Contains (version))
					version = versions.Last ();
				dbg = new CorDebugger(version);
				var lprocess = dbg.DebugActiveProcess((int)processInfo.Id, false);
				lprocess.Continue(new SharpGen.Runtime.Win32.RawBool(false));

				throw new NotImplementedException();
				//SetupProcess(process);
				//process.Continue(false);
			});
			OnStarted();
		}

		protected override void OnContinue ( )
		{
			MtaThread.Run (delegate
			{
				ClearEvalStatus ();
				ClearHandles ();
				process.SetAllThreadsDebugState (CorApi.Portable.CorDebugThreadState.ThreadRun, null);
				process.Continue (false);
			});
		}

		protected override void OnDetach ( )
		{
			MtaThread.Run (delegate
			{
				TerminateDebugger ();
			});
		}

		protected override void OnEnableBreakEvent (BreakEventInfo binfo, bool enable)
		{
			MtaThread.Run (delegate
			{
				var bpList = binfo.Handle as List<CorFunctionBreakpoint>;
				if (bpList != null) {
					foreach (var bp in bpList) {
						try {
							bp.Activate (enable);
						}
						catch (SharpGen.Runtime.SharpGenException e) {
							HandleBreakpointException (binfo, e);
						}
					}
				}
			});
		}

		protected override void OnExit ( )
		{
			MtaThread.Run (delegate
			{
				TerminateDebugger ();
			});
		}

		protected override void OnFinish ( )
		{
			MtaThread.Run (delegate
			{
				if (stepper != null) {
					stepper.StepOut ();
					ClearEvalStatus ();
					process.SetAllThreadsDebugState (CorApi.Portable.CorDebugThreadState.ThreadRun, null);
					process.Continue (false);
				}
			});
		}

		protected override ProcessInfo[] OnGetProcesses ( )
		{
			return MtaThread.Run (() => new ProcessInfo[] { GetProcess (process) });
		}

		protected override Backtrace OnGetThreadBacktrace (long processId, long threadId)
		{
			return MtaThread.Run (delegate
			{
				foreach (CorApi.Portable.Thread t in process.Threads) {
					if (t.Id == threadId) {
						return new Backtrace (new CorBacktrace (t, this));
					}
				}
				return null;
			});
		}

		protected override ThreadInfo[] OnGetThreads (long processId)
		{
			return MtaThread.Run (delegate
			{
				List<ThreadInfo> list = new List<ThreadInfo> ();
				foreach (CorApi.Portable.Thread t in process.Threads)
					list.Add (GetThread (t));
				return list.ToArray ();
			});
		}

		public ISymbolReader GetReaderForModule (CorApi.Portable.Module module)
		{
			lock (appDomainsLock) {
				AppDomainInfo appDomainInfo;
				if (!appDomains.TryGetValue (module.Assembly.AppDomain.Id, out appDomainInfo))
					return null;
				ModuleInfo moduleInfo;
				if (!appDomainInfo.Modules.TryGetValue (module.Name, out moduleInfo))
					return null;
				return moduleInfo.Reader;
			}
		}

		internal CorMetadataImport GetMetadataForModule (CorApi.Portable.Module module)
		{
			lock (appDomainsLock) {
				AppDomainInfo appDomainInfo;
				if (!appDomains.TryGetValue (module.Assembly.AppDomain.Id, out appDomainInfo))
					return null;
				ModuleInfo mod;
				if (!appDomainInfo.Modules.TryGetValue (module.Name, out mod))
					return null;
				return mod.Importer;
			}
		}


		internal IEnumerable<CorApi.Portable.AppDomain> GetAppDomains ()
		{
			lock (appDomainsLock) {
				var corAppDomains = new List<CorApi.Portable.AppDomain> (appDomains.Count);
				foreach (var appDomainInfo in appDomains) {
					corAppDomains.Add (appDomainInfo.Value.AppDomain);
				}
				return corAppDomains;
			}
		}

		internal IEnumerable<CorApi.Portable.Module> GetModules (CorApi.Portable.AppDomain appDomain)
		{
			lock (appDomainsLock) {
				var mods = new List<CorApi.Portable.Module> ();
				AppDomainInfo appDomainInfo;
				if (appDomains.TryGetValue (appDomain.Id, out appDomainInfo)) {
					foreach (ModuleInfo mod in appDomainInfo.Modules.Values) {
						mods.Add (mod.Module);
					}
				}
				return mods;
			}
		}

		internal IEnumerable<CorApi.Portable.Module> GetAllModules ()
		{
			lock (appDomainsLock) {
				var corModules = new List<CorApi.Portable.Module> ();
				foreach (var appDomainInfo in appDomains) {
					corModules.AddRange (GetModules (appDomainInfo.Value.AppDomain));
				}
				return corModules;
			}
		}

		internal CorApi.Portable.HandleValue GetHandle (CorApi.Portable.Value val)
		{
			CorApi.Portable.HandleValue handleVal = null;
			if (!handles.TryGetValue (val.Address, out handleVal)) {
				handleVal = val.CastToHandleValue ();
				if (handleVal == null)
				{
					// Create a handle
					CorApi.Portable.ReferenceValue refVal = val.CastToReferenceValue ();
					CorApi.Portable.HeapValue heapVal = refVal.Dereference ().CastToHeapValue ();
					handleVal = heapVal.CreateHandle (CorApi.Portable.CorDebugHandleType.HandleStrong);
				}
				handles.Add (val.Address, handleVal);	
			}
			return handleVal;
		}

		protected override BreakEventInfo OnInsertBreakEvent (BreakEvent be)
		{
			return MtaThread.Run (delegate {
				var binfo = new BreakEventInfo ();
				var bp = be as Breakpoint;
				if (bp != null) {
					if (bp is FunctionBreakpoint) {
						// FIXME: implement breaking on function name
						binfo.SetStatus (BreakEventStatus.Invalid, "Function breakpoint is not implemented");
						return binfo;
					}
					else {
						var docInfos = new List<DocInfo> ();
						lock (appDomainsLock) {
							foreach (var appDomainInfo in appDomains) {
								var documents = appDomainInfo.Value.Documents;
								DocInfo docInfo = null;
								if (documents.TryGetValue (Path.GetFullPath (bp.FileName), out docInfo)) {
									docInfos.Add (docInfo);
								}
							}
						}

						var doc = docInfos.FirstOrDefault (); //get info about source position using SymbolReader of first DocInfo

						if (doc == null) {
							binfo.SetStatus (BreakEventStatus.NotBound, string.Format("{0} is not found among the loaded symbol documents", bp.FileName));
							return binfo;
						}
						int line;
						try {
							line = doc.Document.FindClosestLine (bp.Line);
							bp.SetLine(line);
						} catch {
							// Invalid line
							binfo.SetStatus (BreakEventStatus.Invalid, string.Format("Invalid line {0}", bp.Line));
							return binfo;
						}
						ISymbolMethod[] methods = null;
						if (doc.ModuleInfo.Reader is ISymbolReader2) {
							methods = ((ISymbolReader2)doc.ModuleInfo.Reader).GetMethodsFromDocumentPosition (doc.Document, line, 0);
						}
						if (methods == null || methods.Length == 0) {
							var met = doc.ModuleInfo.Reader.GetMethodFromDocumentPosition (doc.Document, line, 0);
							if (met != null)
								methods = new ISymbolMethod[] {met};
						}

						if (methods == null || methods.Length == 0) {
							binfo.SetStatus (BreakEventStatus.Invalid, "Unable to resolve method at position");
							return binfo;
						}

						ISymbolMethod bestMethod = null;
						ISymbolMethod bestLeftSideMethod = null;
						ISymbolMethod bestRightSideMethod = null;

						SequencePoint bestSp = null;
						SequencePoint bestLeftSideSp = null;
						SequencePoint bestRightSideSp = null;

						foreach (var met in methods) {
							foreach (SequencePoint sp in met.GetSequencePoints ()) {
								if (sp.IsInside (doc.Document.URL, line, bp.Column)) {	//breakpoint is inside current sequence point
									if (bestSp == null || bestSp.IsInside (doc.Document.URL, sp.StartLine, sp.StartColumn)) {	//and sp is inside of current candidate
										bestSp = sp;
										bestMethod = met;
										break;
									}
								} else if (sp.StartLine == line
								           && sp.Document.URL.Equals (doc.Document.URL, StringComparison.OrdinalIgnoreCase)
								           && sp.StartColumn <= bp.Column) {	//breakpoint is on the same line and on the right side of sp
									if (bestLeftSideSp == null
									    || bestLeftSideSp.EndColumn < sp.EndColumn) {
										bestLeftSideSp = sp;
										bestLeftSideMethod = met;
									}
								} else if (sp.StartLine >= line
								           && sp.Document.URL.Equals (doc.Document.URL, StringComparison.OrdinalIgnoreCase)) {	//sp is after bp
									if (bestRightSideSp == null
									    || bestRightSideSp.StartLine > sp.StartLine
									    || (bestRightSideSp.StartLine == sp.StartLine && bestRightSideSp.StartColumn > sp.StartColumn)) { //and current candidate is on the right side of it
										bestRightSideSp = sp;
										bestRightSideMethod = met;
									}
								}
							}
						}

						SequencePoint bestSameLineSp;
						ISymbolMethod bestSameLineMethod;

						if (bestRightSideSp != null
						    && (bestLeftSideSp == null
						        || bestRightSideSp.StartLine > line)) {
							bestSameLineSp = bestRightSideSp;
							bestSameLineMethod = bestRightSideMethod;
						}
						else {
							bestSameLineSp = bestLeftSideSp;
							bestSameLineMethod = bestLeftSideMethod;
						}

						if (bestSameLineSp != null) {
							if (bestSp == null) {
								bestSp = bestSameLineSp;
								bestMethod = bestSameLineMethod;
							}
							else {
								if (bp.Line != bestSp.StartLine || bestSp.StartColumn != bp.Column) {
									bestSp = bestSameLineSp;
									bestMethod = bestSameLineMethod;
								}
							}
						}

						if (bestSp == null || bestMethod == null) {
							binfo.SetStatus (BreakEventStatus.Invalid, "Unable to calculate an offset in IL code");
							return binfo;
						}

						foreach (var docInfo in docInfos) {
							CorApi.Portable.Function func = docInfo.ModuleInfo.Module.GetFunctionFromToken ((uint)bestMethod.Token.GetToken ());

							try {
								CorApi.Portable.FunctionBreakpoint corBp = func.ILCode.CreateBreakpoint ((uint)bestSp.Offset);
								breakpoints[corBp] = binfo;

								if (binfo.Handle == null)
									binfo.Handle = new List<CorApi.Portable.FunctionBreakpoint> ();
								(binfo.Handle as List<CorApi.Portable.FunctionBreakpoint>).Add (corBp);
								corBp.Activate (bp.Enabled);
								binfo.SetStatus (BreakEventStatus.Bound, null);
							}
							catch (SharpGen.Runtime.SharpGenException e) {
								HandleBreakpointException (binfo, e);
							}
						}
						return binfo;
					}
				}

				var cp = be as Catchpoint;
				if (cp != null) {
					var bound = false;
					lock (appDomainsLock) {
						foreach (var appDomainInfo in appDomains) {
							foreach (ModuleInfo mod in appDomainInfo.Value.Modules.Values) {
								CorMetadataImport mi = mod.Importer;
								if (mi != null) {
									foreach (Type t in mi.DefinedTypes)
										if (t.FullName == cp.ExceptionName) {
											bound = true;
										}
								}
							}
						}
					}
					if (bound) {
						binfo.SetStatus (BreakEventStatus.Bound, null);
						return binfo;
					}
				}

				binfo.SetStatus (BreakEventStatus.Invalid, null);
				return binfo;
			});
		}

		private static void HandleBreakpointException (BreakEventInfo binfo, SharpGen.Runtime.SharpGenException e)
		{
			var code = e.ToHResult<HResult> ();
			if (code != null) {
				switch (code) {
					case HResult.CORDBG_E_UNABLE_TO_SET_BREAKPOINT:
						binfo.SetStatus (BreakEventStatus.Invalid, "Invalid breakpoint position");
						break;
					case HResult.CORDBG_E_PROCESS_TERMINATED:
						binfo.SetStatus (BreakEventStatus.BindError, "Process terminated");
						break;
					case HResult.CORDBG_E_CODE_NOT_AVAILABLE:
						binfo.SetStatus (BreakEventStatus.BindError, "Module is not loaded");
						break;
					default:
						binfo.SetStatus (BreakEventStatus.BindError, e.Message);
						break;
				}
			}
			else {
				binfo.SetStatus (BreakEventStatus.BindError, e.Message);
				DebuggerLoggingService.LogError ("Unknown exception when setting breakpoint", e);
			}
		}

		protected override void OnCancelAsyncEvaluations ()
		{
			ObjectAdapter.CancelAsyncOperations ();
		}

		protected override void OnNextInstruction ( )
		{
			MtaThread.Run (delegate {
				Step (false);
			});
		}

		protected override void OnNextLine ( )
		{
			MtaThread.Run (delegate
			{
				Step (false);
			});
		}

		void Step (bool into)
		{
			try {
				ObjectAdapter.CancelAsyncOperations ();
				if (stepper != null) {
					var frame = activeThread.ActiveFrame;
					ISymbolReader reader = GetReaderForModule (frame.Function.Module);
					if (reader == null) {
						RawContinue (into);
						return;
					}
					ISymbolMethod met = reader.GetMethod (new SymbolToken ((int)frame.Function.Token));
					if (met == null) {
						RawContinue (into);
						return;
					}

					int offset;
					CorApi.Portable.CorDebugMappingResult mappingResult;
					
					frame.GetIP (out offset, out mappingResult);

					// Exclude all ranges belonging to the current line
					var ranges = new List<CorApi.Portable.CorDebugStepRange> ();
					var sequencePoints = met.GetSequencePoints ().ToArray ();
					for (int i = 0; i < sequencePoints.Length; i++) {
						if (sequencePoints [i].Offset > offset) {
							var r = new CorApi.Portable.CorDebugStepRange();
							r.StartOffset = i == 0 ? 0 : sequencePoints [i - 1].Offset;
							r.EndOffset = sequencePoints [i].Offset;
							ranges.Add (r);
							break;
						}
					}
					if (ranges.Count == 0 && sequencePoints.Length > 0) {
						var r = new CorApi.Portable.CorDebugStepRange();
						r.StartOffset = sequencePoints [sequencePoints.Length - 1].Offset;
						r.EndOffset = int.MaxValue;
						ranges.Add (r);
					}

					stepper.StepRange (into, ranges.ToArray ());

					ClearEvalStatus ();
					process.SetAllThreadsDebugState (CorApi.Portable.CorDebugThreadState.ThreadRun, null);
					process.Continue (false);
				}
			} catch (Exception e) {
				DebuggerLoggingService.LogError ("Exception on Step()", e);
			}
		}

		private void RawContinue (bool into, bool stepOverAll = false)
		{
			if (stepOverAll)
				stepper.StepRange (into, new[]{ new CorApi.Portable.CorDebugStepRange (){ StartOffset = 0, EndOffset = int.MaxValue } });
			else
				stepper.Step (into);
			ClearEvalStatus ();
			process.Continue (false);
		}

		protected override void OnRemoveBreakEvent (BreakEventInfo bi)
		{
			if (terminated)
				return;
			
			if (bi.Status != BreakEventStatus.Bound || bi.Handle == null)
				return;

			MtaThread.Run (delegate
			{
				var corBpList = (List<CorApi.Portable.FunctionBreakpoint>)bi.Handle;
				foreach (var corBp in corBpList) {
					try {
						corBp.Activate (false);
					}
					catch (SharpGen.Runtime.SharpGenException e) {
						HandleBreakpointException (bi, e);
					}
				}
			});
		}


		protected override void OnSetActiveThread (long processId, long threadId)
		{
			MtaThread.Run (delegate
			{
				activeThread = null;
				if (stepper != null && stepper.IsActive)
					stepper.Deactivate ();
				stepper = null;
				foreach (CorApi.Portable.Thread t in process.Threads) {
					if (t.Id == threadId) {
						SetActiveThread (t);
						break;
					}
				}
			});
		}

		void SetActiveThread (CorApi.Portable.Thread t)
		{
			activeThread = t;
			if (stepper != null && stepper.IsActive) {
				stepper.Deactivate ();
			}
			stepper = activeThread.CreateStepper ();
			stepper.SetUnmappedStopMask(CorApi.Portable.CorDebugUnmappedStop.StopNone);
			stepper.SetJmcStatus (true);
		}

		protected override void OnStepInstruction ( )
		{
			MtaThread.Run (delegate {
				Step (true);
			});
		}

		protected override void OnStepLine ( )
		{
			MtaThread.Run (delegate
			{
				Step (true);
			});
		}

		protected override void OnStop ( )
		{
			TargetEventArgs args = new TargetEventArgs (TargetEventType.TargetStopped);

			MtaThread.Run (delegate
			{
				process.Stop (0);
				OnStopped ();
				CorApi.Portable.Thread currentThread = null;
				foreach (CorApi.Portable.Thread t in process.Threads) {
					currentThread = t;
					break;
				}
				args.Process = GetProcess (process);
				args.Thread = GetThread (currentThread);
				args.Backtrace = new Backtrace (new CorBacktrace (currentThread, this));
			});
			OnTargetEvent (args);
		}

		protected override void OnUpdateBreakEvent (BreakEventInfo be)
		{
		}

		public CorApi.Portable.Value RuntimeInvoke (CorEvaluationContext ctx, CorApi.Portable.Function function, CorApi.Portable.Type[] typeArgs, CorApi.Portable.Value thisObj, CorApi.Portable.Value[] arguments)
		{
			CorApi.Portable.Value[] args;
			if (thisObj == null)
				args = arguments;
			else {
				args = new CorApi.Portable.Value[arguments.Length + 1];
				args[0] = thisObj;
				arguments.CopyTo (args, 1);
			}

			var methodCall = new CorMethodCall (ctx, function, typeArgs, args);
			try {
				var result = ObjectAdapter.InvokeSync (methodCall, ctx.Options.EvaluationTimeout);
				if (result.ResultIsException) {
					var vref = new CorValRef (result.Result);
					throw new EvaluatorExceptionThrownException (vref, ObjectAdapter.GetValueTypeName (ctx, vref));
				}

				WaitUntilStopped ();
				return result.Result;
			}
			catch (SharpGen.Runtime.SharpGenException ex) {
				// eval exception is a 'good' exception that should be shown in value box
				// all other exceptions must be thrown to error log
				var evalException = TryConvertToEvalException (ex);
				if (evalException != null)
					throw evalException;
				throw;
			}
		}

		internal void OnStartEvaluating ( )
		{
			lock (debugLock) {
				evaluating = true;
			}
		}

		internal void OnEndEvaluating ( )
		{
			lock (debugLock) {
				evaluating = false;
				Monitor.PulseAll (debugLock);
			}
		}

		CorApi.Portable.Value NewSpecialObject (CorEvaluationContext ctx, Action<CorApi.Portable.Eval> createCall)
		{
			ManualResetEvent doneEvent = new ManualResetEvent (false);
			CorApi.Portable.Value result = null;
			var eval = ctx.Eval;
			CorApi.Portable.DebugEventHandler<CorApi.Portable.EvalEventArgs> completeHandler = delegate (object o, CorApi.Portable.EvalEventArgs eargs) {
				if (eargs.Eval != eval)
					return;
				result = eargs.Eval.Result;
				doneEvent.Set ();
				eargs.Continue = false;
			};

			CorApi.Portable.DebugEventHandler<CorApi.Portable.EvalEventArgs> exceptionHandler = delegate(object o, CorApi.Portable.EvalEventArgs eargs)
			{
				if (eargs.Eval != eval)
					return;
				result = eargs.Eval.Result;
				doneEvent.Set ();
				eargs.Continue = false;
			};
			process.OnEvalComplete += completeHandler;
			process.OnEvalException += exceptionHandler;

			try {
				createCall (eval);
				process.SetAllThreadsDebugState (CorApi.Portable.CorDebugThreadState.ThreadSuspend, ctx.Thread);
				OnStartEvaluating ();
				ClearEvalStatus ();
				process.Continue (false);

				if (doneEvent.WaitOne (ctx.Options.EvaluationTimeout, false))
					return result;
				else {
					eval.Abort ();
					return null;
				}
			}
			catch (SharpGen.Runtime.SharpGenException ex) {
				var evalException = TryConvertToEvalException (ex);
				// eval exception is a 'good' exception that should be shown in value box
				// all other exceptions must be thrown to error log
				if (evalException != null)
					throw evalException;
				throw;
			}
			finally {
				process.OnEvalComplete -= completeHandler;
				process.OnEvalException -= exceptionHandler;
				OnEndEvaluating ();
			}
		}

		public CorApi.Portable.Value NewString (CorEvaluationContext ctx, string value)
		{
			return NewSpecialObject (ctx, eval => eval.NewString (value));
		}

		public CorApi.Portable.Value NewArray (CorEvaluationContext ctx, CorApi.Portable.Type elemType, int size)
		{
			return NewSpecialObject (ctx, eval => eval.NewParameterizedArray (elemType, 1, 1, 0));
		}

		private static EvaluatorException TryConvertToEvalException (SharpGen.Runtime.SharpGenException ex)
		{
			var hResult = (HResult)ex.HResult;
			string message = null;
			switch (hResult) {
				case HResult.CORDBG_E_ILLEGAL_AT_GC_UNSAFE_POINT:
					message = "The thread is not at a GC-safe point";
					break;
				case HResult.CORDBG_E_ILLEGAL_IN_PROLOG:
					message = "The thread is in the prolog";
					break;
				case HResult.CORDBG_E_ILLEGAL_IN_NATIVE_CODE:
					message = "The thread is in native code";
					break;
				case HResult.CORDBG_E_ILLEGAL_IN_OPTIMIZED_CODE:
					message = "The thread is in optimized code";
					break;
				case HResult.CORDBG_E_FUNC_EVAL_BAD_START_POINT:
					message = "Bad starting point to perform evaluation";
					break;
			}
			if (message != null)
				return new EvaluatorException ("Evaluation is not allowed: {0}", message);
			return null;
		}


		public void WaitUntilStopped ()
		{
			lock (debugLock) {
				while (evaluating)
					Monitor.Wait (debugLock);
			}
		}

		internal void ClearEvalStatus ( )
		{
			foreach (CorApi.Portable.Process p in dbg.Processes) {
				if (p.Id == processId) {
					process = p;
					break;
				}
			}
		}
		
		void ClearHandles ( )
		{
			foreach (CorApi.Portable.HandleValue handle in handles.Values) {
				handle.Dispose ();
			}
			handles.Clear ();
		}

		ProcessInfo GetProcess (CorApi.Portable.Process proc)
		{
			ProcessInfo info;
			lock (processes) {
				if (!processes.TryGetValue (proc.Id, out info)) {
					info = new ProcessInfo (proc.Id, "");
					processes[proc.Id] = info;
				}
			}
			return info;
		}

		ThreadInfo GetThread (CorApi.Portable.Thread thread)
		{
			ThreadInfo info;
			lock (threads) {
				if (!threads.TryGetValue (thread.Id, out info)) {
					string loc = string.Empty;
					try {
						if (thread.ActiveFrame != null) {
							StackFrame frame = CorBacktrace.CreateFrame (this, thread.ActiveFrame);
							loc = frame.ToString ();
						}
						else {
							loc = "<Unknown>";
						}
					}
					catch {
						loc = "<Unknown>";
					}
					
					info = new ThreadInfo (thread.Process.Id, thread.Id, GetThreadName (thread), loc);
					threads[thread.Id] = info;
				}
				return info;
			}
		}

		public CorApi.Portable.Thread GetThread (ulong id)
		{
			try {
				WaitUntilStopped ();
				foreach (CorApi.Portable.Thread t in process.Threads)
					if (t.Id == id)
						return t;
				throw new InvalidOperationException ("Invalid thread id " + id);
			}
			catch {
				throw;
			}
		}

		string GetThreadName (CorApi.Portable.Thread thread)
		{
			// From http://social.msdn.microsoft.com/Forums/en/netfxtoolsdev/thread/461326fe-88bd-4a6b-82a9-1a66b8e65116
		    try 
		    {
		        CorApi.Portable.ReferenceValue refVal = thread.ThreadVariable.CastToReferenceValue(); 
		        if (refVal.IsNull) 
		            return string.Empty;
				
		        CorApi.Portable.ObjectValue val = refVal.Dereference().CastToObjectValue(); 
		        if (val != null) 
		        { 
					Type classType = val.ExactType.GetTypeInfo (this);
		            // Loop through all private instance fields in the thread class 
		            foreach (FieldInfo fi in classType.GetFields (BindingFlags.NonPublic | BindingFlags.Instance))
		            { 
		                if (fi.Name == "m_Name")
						{
		                        var fieldValue = val.GetFieldValue(val.Class, (uint)fi.MetadataToken).CastToReferenceValue(); 
							
								if (fieldValue.IsNull)
									return string.Empty;
								else
									return fieldValue.Dereference().CastToStringValue().String;
		                } 
		            } 
		        } 
		    } catch (Exception) {
				// Ignore
			}
			
			return string.Empty;
		}
		
		string EvaluateTrace (CorApi.Portable.Thread thread, string exp)
		{
			StringBuilder sb = new StringBuilder ();
			int last = 0;
			int i = exp.IndexOf ('{');
			while (i != -1) {
				if (i < exp.Length - 1 && exp [i+1] == '{') {
					sb.Append (exp.Substring (last, i - last + 1));
					last = i + 2;
					i = exp.IndexOf ('{', i + 2);
					continue;
				}
				int j = exp.IndexOf ('}', i + 1);
				if (j == -1)
					break;
				string se = exp.Substring (i + 1, j - i - 1);
				try {
					se = EvaluateExpression (thread, se);
				}
				catch (EvaluatorException e) {
					OnDebuggerOutput (false, e.ToString ());
					return String.Empty;
				}
				sb.Append (exp.Substring (last, i - last));
				sb.Append (se);
				last = j + 1;
				i = exp.IndexOf ('{', last);
			}
			sb.Append (exp.Substring (last, exp.Length - last));
			return sb.ToString ();
		}
		
		string EvaluateExpression (CorApi.Portable.Thread thread, string exp)
		{
			try {
				if (thread.ActiveFrame == null)
					return string.Empty;
				EvaluationOptions ops = Options.EvaluationOptions.Clone ();
				ops.AllowTargetInvoke = true;
				CorEvaluationContext ctx = new CorEvaluationContext (this, new CorBacktrace (thread, this), 0, ops);
				ctx.Thread = thread;
				ValueReference val = ctx.Evaluator.Evaluate (ctx, exp);
				return val.CreateObjectValue (false).Value;
			}
			catch (EvaluatorException) {
				throw;
			}
			catch (Exception ex) {
				throw new EvaluatorException (ex.Message);
			}
		}

		protected override T OnWrapDebuggerObject<T> (T obj)
		{
			if (obj is IBacktrace)
				return (T) (object) new MtaBacktrace ((IBacktrace)obj);
			if (obj is IObjectValueSource)
				return (T)(object)new MtaObjectValueSource ((IObjectValueSource)obj);
			if (obj is IObjectValueUpdater)
				return (T)(object)new MtaObjectValueUpdater ((IObjectValueUpdater)obj);
			if (obj is IRawValue)
				return (T)(object)new MtaRawValue ((IRawValue)obj);
			if (obj is IRawValueArray)
				return (T)(object)new MtaRawValueArray ((IRawValueArray)obj);
			if (obj is IRawValueString)
				return (T)(object)new MtaRawValueString ((IRawValueString)obj);
			return obj;
		}

		public override bool CanSetNextStatement {
			get {
				return true;
			}
		}

		protected override void OnSetNextStatement (long threadId, string fileName, int line, int column)
		{
			if (!CanSetNextStatement)
				throw new NotSupportedException ();
			MtaThread.Run (delegate {
				var thread = GetThread ((ulong)threadId);
				if (thread == null)
					throw new ArgumentException ("Unknown thread.");

				var frame = thread.ActiveFrame;
				if (frame == null)
					throw new NotSupportedException ();
			
				ISymbolMethod met = frame.Function.GetSymbolMethod (this);
				if (met == null) {
					throw new NotSupportedException ();
				}

				int offset = -1;
				int firstSpInLine = -1;
				foreach (SequencePoint sp in met.GetSequencePoints ()) {
					if (sp.IsInside (fileName, line, column)) {
						offset = (int)sp.Offset;
						break;
					} else if (firstSpInLine == -1
					           && sp.StartLine == line
					           && sp.Document.URL.Equals (fileName, StringComparison.OrdinalIgnoreCase)) {
						firstSpInLine = (int)sp.Offset;
					}
				}
				if (offset == -1) {//No exact match? Use first match in that line
					offset = firstSpInLine;
				}
				if (offset == -1) {
					throw new NotSupportedException ();
				}
				try {
					frame.SetIP (offset);
					OnStopped ();
					RaiseStopEvent ();
				} catch {
					throw new NotSupportedException ();
				}
			});
		}
	}

	class SequencePoint
	{
		public int StartLine;
		public int EndLine;
		public int StartColumn;
		public int EndColumn;
		public uint Offset;
		public bool IsSpecial;
		public ISymbolDocument Document;

		public bool IsInside (string fileUrl, int line, int column)
		{
			if (!Document.URL.Equals (fileUrl, StringComparison.OrdinalIgnoreCase))
				return false;
			if (line < StartLine || (line == StartLine && column < StartColumn))
				return false;
			if (line > EndLine || (line == EndLine && column > EndColumn))
				return false;
			return true;
		}
	}

	static class SequencePointExt
	{
		public static IEnumerable<SequencePoint> GetSequencePoints (this ISymbolMethod met)
		{
			int sc = met.SequencePointCount;
			int[] offsets = new int[sc];
			int[] lines = new int[sc];
			int[] endLines = new int[sc];
			int[] columns = new int[sc];
			int[] endColumns = new int[sc];
			ISymbolDocument[] docs = new ISymbolDocument[sc];
			met.GetSequencePoints (offsets, docs, lines, columns, endLines, endColumns);

			for (int n = 0; n < sc; n++) {
				SequencePoint sp = new SequencePoint ();
				sp.Document = docs[n];
				sp.StartLine = lines[n];
				sp.EndLine = endLines[n];
				sp.StartColumn = columns[n];
				sp.EndColumn = endColumns[n];
				sp.Offset = (uint)offsets[n];
				yield return sp;
			}
		}

		public static Type GetTypeInfo (this CorApi.Portable.Type type, CorDebuggerSession session)
		{
			Type t;
			if (MetadataHelperFunctionsExtensions.CoreTypes.TryGetValue ((CorApi.Portable.CorElementType)type.CorType, out t))
				return t;

			if (type.CorType == CorApi.Portable.CorElementType.ElementTypeArray || type.CorType == CorApi.Portable.CorElementType.ElementTypeSzarray) {
				List<int> sizes = new List<int> ();
				List<int> loBounds = new List<int> ();
				for (int n = 0; n < type.Rank; n++) {
					sizes.Add (1);
					loBounds.Add (0);
				}
				return MetadataExtensions.MakeArray (type.FirstTypeParameter.GetTypeInfo (session), sizes, loBounds);
			}

			if (type.CorType == CorApi.Portable.CorElementType.ElementTypeByref)
				return MetadataExtensions.MakeByRef (type.FirstTypeParameter.GetTypeInfo (session));

			if (type.CorType == CorApi.Portable.CorElementType.ElementTypePtr)
				return MetadataExtensions.MakePointer (type.FirstTypeParameter.GetTypeInfo (session));

			CorMetadataImport mi = session.GetMetadataForModule (type.Class.Module);
			if (mi != null) {
				t = mi.GetType (type.Class.Token);
				CorApi.Portable.Type[] targs = type.TypeParameters;
				if (targs.Length > 0) {
					List<Type> types = new List<Type> ();
					foreach (CorApi.Portable.Type ct in targs)
						types.Add (ct.GetTypeInfo (session));
					return MetadataExtensions.MakeGeneric (t, types);
				}
				else
					return t;
			}
			else
				return null;
		}

		public static ISymbolMethod GetSymbolMethod (this CorApi.Portable.Function func, CorDebuggerSession session)
		{
			ISymbolReader reader = session.GetReaderForModule (func.Module);
			if (reader == null)
				return null;
			return reader.GetMethod (new SymbolToken ((int)func.Token));
		}

		public static MethodInfo GetMethodInfo (this CorApi.Portable.Function func, CorDebuggerSession session)
		{
			CorMetadataImport mi = session.GetMetadataForModule (func.Module);
			if (mi != null)
				return mi.GetMethodInfo (func.Token);
			else
				return null;
		}

		public static void SetValue (this CorValRef thisVal, EvaluationContext ctx, CorValRef val)
		{
			CorEvaluationContext cctx = (CorEvaluationContext) ctx;
			CorObjectAdaptor actx = (CorObjectAdaptor) ctx.Adapter;
			if (actx.IsEnum (ctx, thisVal.Val.ExactType) && !actx.IsEnum (ctx, val.Val.ExactType)) {
				ValueReference vr = actx.GetMember (ctx, null, thisVal, "value__");
				vr.Value = val;
				// Required to make sure that var returns an up-to-date value object
				thisVal.Invalidate ();
				return;
			}

			var s = thisVal.Val.CastToReferenceValue ();
			if (s != null) {
				var v = val.Val.CastToReferenceValue ();
				if (v != null) {
					throw new NotImplementedException();
					//s.Value = v.Value;
					return;
				}
			}
			var gv = CorObjectAdaptor.GetRealObject (cctx, thisVal.Val) as CorApi.Portable.GenericValue;
			if (gv != null)
				gv.SetValue (ctx.Adapter.TargetObjectToObject (ctx, val));
		}
	}

	public interface ICustomCorSymbolReaderFactory
	{
		ISymbolReader CreateCustomSymbolReader (string assemblyInfo);
	}
}
