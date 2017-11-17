using AvalonStudio.Extensibility.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mono.Debugging.Win32
{
	public class CustomSyncContext : SynchronizationContext
	{
		private JobRunner _runner;

		public CustomSyncContext (JobRunner runner)
		{
			_runner = runner;
		}

		public override void Send(SendOrPostCallback d, object state)
		{
			if (Thread.CurrentThread == _runner.MainThread)
			{
				d(state);
			}
			else
			{
				_runner.InvokeAsync(() => d(state)).Wait();
			}
		}

		public override void Post(SendOrPostCallback d, object state)
		{
			_runner.InvokeAsync(() => d(state));
		}
	}

	public static class MtaThread
	{
		static readonly AutoResetEvent wordDoneEvent = new AutoResetEvent(false);
		static Action workDelegate;
		static readonly object workLock = new object();
		static Thread workThread;
		static Exception workError;
		static readonly object threadLock = new object();
		static JobRunner runner;

		public static R Run<R>(Func<R> ts, int timeout = 15000)
		{
			if (AvalonStudio.Platforms.Platform.PlatformIdentifier == AvalonStudio.Platforms.PlatformID.Win32NT)
			{
				if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA)
				{
					return ts();					
				}
			}
			else if(Thread.CurrentThread == runner.MainThread)
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
			if (AvalonStudio.Platforms.Platform.PlatformIdentifier == AvalonStudio.Platforms.PlatformID.Win32NT)
			{
				if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA)
				{
					ts();
					return;
				}
			}
			else if(Thread.CurrentThread == runner.MainThread)
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

						if (AvalonStudio.Platforms.Platform.PlatformIdentifier == AvalonStudio.Platforms.PlatformID.Win32NT)
						{
							workThread.SetApartmentState(ApartmentState.MTA);
						}

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
			runner = new JobRunner();

			SynchronizationContext.SetSynchronizationContext(new CustomSyncContext(runner));
			runner.RunLoop(new CancellationToken());
		}
	}
}
