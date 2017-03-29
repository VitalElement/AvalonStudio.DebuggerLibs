using Mono.Debugging.Client;
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

            var startInfo = new DebuggerStartInfo() {

                UseExternalConsole = true,
                CloseExternalConsoleOnExit = true
            };

            var sessionOptions = EvaluationOptions.DefaultOptions.Clone();

            sessionOptions.EllipsizeStrings = false;
            sessionOptions.GroupPrivateMembers = false;
            sessionOptions.EvaluationTimeout = 1000;

            session.Run( startInfo, new DebuggerSessionOptions() { EvaluationOptions = sessionOptions });
        }
    }
}
