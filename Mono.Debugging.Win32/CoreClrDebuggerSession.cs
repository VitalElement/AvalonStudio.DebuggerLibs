using System;
using Microsoft.Samples.Debugging.CorDebug;
using Mono.Debugging.Client;

namespace Mono.Debugging.Win32
{
	public class CoreClrDebuggerSession : CorDebuggerSession
	{
		static readonly TimeSpan RuntimeLoadTimeout = TimeSpan.FromSeconds (1);

		public CoreClrDebuggerSession (char[] badPathChars) : base (badPathChars)
		{
		}

		protected override void OnRun (DebuggerStartInfo startInfo)
		{
			MtaThread.Run (() => {
				var workingDir = PrepareWorkingDirectory (startInfo);
				var env = PrepareEnvironment (startInfo);
				var cmd = PrepareCommandLine (startInfo);
				int procId;
				var iCorDebug = CoreClrShimUtil.CreateICorDebugForCommand (cmd, workingDir, env, RuntimeLoadTimeout, out procId);
				dbg = new CorDebugger (iCorDebug);
				process = dbg.DebugActiveProcess (procId, false);
				processId = process.Id;
				SetupProcess (process);
				process.Continue (false);
			});
			OnStarted();
		}

		protected override void OnAttachToProcess (long procId)
		{
			AttachToProcessImpl ((int) procId);
		}

		protected override void OnAttachToProcess (ProcessInfo processInfo)
		{
			AttachToProcessImpl ((int) processInfo.Id);
		}

		void AttachToProcessImpl (int procId)
		{
			attaching = true;
			MtaThread.Run(delegate
			{
				var iCorDebug = CoreClrShimUtil.CreateICorDebugForProcess (procId, RuntimeLoadTimeout);
				dbg = new CorDebugger(iCorDebug);
				process = dbg.DebugActiveProcess(procId, false);
				SetupProcess(process);
				process.Continue(false);
			});
			OnStarted();
		}
	}
}