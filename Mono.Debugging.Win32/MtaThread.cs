using AvalonStudio.Extensibility.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mono.Debugging.Win32
{
	public static class MtaThread
	{
		static readonly AutoResetEvent wordDoneEvent = new AutoResetEvent(false);
		static Action workDelegate;
		static readonly object workLock = new object();
		static Thread workThread;
		static Exception workError;
		static readonly object threadLock = new object();
		static JobRunner runner = new JobRunner();

		public static R Run<R>(Func<R> ts, int timeout = 15000)
		{
			if (Thread.CurrentThread != runner.MainThread)
			{
				return ts();
			}

			R res = default(R);
			Run(delegate
			{
				res = ts();
			}, timeout);
			return res;
		}

		public static void Run(Action ts, int timeout = 15000)
		{
			if (Thread.CurrentThread != workThread)
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
		}

		static void MtaRunner()
		{
			runner.RunLoop(new CancellationToken());
		}
	}
}
