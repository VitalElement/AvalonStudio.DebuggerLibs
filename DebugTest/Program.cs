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

                Command = "dotnet.exe",
                Arguments = "c:\\dev\\repos\\dotnettest\\bin\\Debug\\netcoreapp1.1\\dotnettest.dll",
                WorkingDirectory = "c:\\dev\\repos\\dotnettest\\bin\\Debug\\netcoreapp1.1\\",
                UseExternalConsole = true,
            };

            session.CustomSymbolReaderFactory = new PdbSymbolReaderFactory();

            session.Breakpoints.Add("c:\\dev\\repos\\dotnettest\\Program.cs", 7);

            session.TargetStarted += (sender, e) =>
            {
                Console.WriteLine("Target started.");
            };

            session.TargetHitBreakpoint += (sender, e) =>
            {
                Console.WriteLine("Breakpoint hit.");

                session.StepLine();
            };

            session.TargetEvent += (sender, e) =>
            {
                Console.WriteLine(e.Type.ToString());
            };

            var sessionOptions = EvaluationOptions.DefaultOptions.Clone();

            sessionOptions.EllipsizeStrings = false;
            sessionOptions.GroupPrivateMembers = false;
            sessionOptions.EvaluationTimeout = 1000;

            session.Run( startInfo, new DebuggerSessionOptions() { EvaluationOptions = sessionOptions });
        }
    }
}
