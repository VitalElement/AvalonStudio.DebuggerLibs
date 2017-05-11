using AvalonStudio.Extensibility.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mono.Debugging.Win32
{
	public static class MtaThread
	{
		static readonly AutoResetEvent wordDoneEvent = new AutoResetEvent (false);
		static Action workDelegate;
		static readonly object workLock = new object ();
		static Thread workThread;
		static Exception workError;
		static readonly object threadLock = new object ();
		static JobRunner runner = new JobRunner();

		public static R Run<R> (Func<R> ts, int timeout = 15000)
		{
			if (Thread.CurrentThread != runner.MainThread)
				return ts ();

			R res = default (R);
			Run (delegate {
				res = ts ();
			}, timeout);
			return res;
		}

		public static void Run (Action ts, int timeout = 15000)
		{
			if(Thread.CurrentThread != runner.MainThread)
			{
				ts();
				return;
			}

			lock (workLock)
			{
				lock (threadLock)
				{
					workDelegate = ts;
					workError = null;
					if (workThread == null)
					{
						workThread = new Thread(MtaRunner);
						workThread.Name = "Win32 Debugger MTA Thread";
						workThread.IsBackground = true;
						workThread.Start();
					}
				}
			}

			runner.InvokeAsync(ts);


			//lock (workLock) {
			//	lock (threadLock) {
			//		workDelegate = ts;
			//		workError = null;
			//		if (workThread == null) {
			//			workThread = new Thread (MtaRunner);
			//			workThread.Name = "Win32 Debugger MTA Thread";
			//			//workThread.SetApartmentState (ApartmentState.MTA);
			//			workThread.IsBackground = true;
			//			workThread.Start ();
			//		} else
			//			// Awaken the existing thread
			//			Monitor.Pulse (threadLock);
			//	}
			//	if (!wordDoneEvent.WaitOne (timeout)) {
			//		//workThread.Abort ();
			//		throw new Exception ("Debugger operation timeout on MTA thread.");
			//	}
			//}
			//if (workError != null)
			//	throw workError;
		}

		static void MtaRunner ()
		{
			runner.RunLoop(new CancellationToken());
		//	try {
		//		lock (threadLock) {
		//			do {
		//				try {
		//					workDelegate ();
		//				} catch (ThreadAbortException) {
		//					return;
		//				} catch (Exception ex) {
		//					workError = ex;
		//				} finally {
		//					workDelegate = null;
		//				}
		//				wordDoneEvent.Set ();
		//			} while (Monitor.Wait (threadLock, 60000));

		//		}
		//	} catch {
		//		//Just in case if we abort just in moment when it leaves workDelegate ();
		//	} finally {
		//		workThread = null;
		//	}
		}
	}
}
