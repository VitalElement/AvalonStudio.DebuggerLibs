namespace Mono.Debugging.Client
{
	public class ClrProcessInfo: ProcessInfo
	{
		private string runtime;

		public ClrProcessInfo (long id, string name, string runtime) : base (id, name)
		{
			this.runtime = runtime;
		}

		public string Runtime {
			get {
				return runtime;
			}
		}
	}
}