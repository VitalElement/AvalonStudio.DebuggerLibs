using Mono.Debugging.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DebugTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var session = new CoreClrDebuggerSession(Path.GetInvalidPathChars(), "C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App\\1.1.1\\dbgshim.dll");

            session.Run(new Mono.Debugging.Client.DebuggerStartInfo() { }, new Mono.Debugging.Client.DebuggerSessionOptions() { });
        }
    }
}
