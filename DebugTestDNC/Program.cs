using Microsoft.Samples.Debugging.CorDebug;
using Mono.Debugging.Client;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DebugTestDNC
{
    class Program
    {
        static readonly TimeSpan RuntimeLoadTimeout = TimeSpan.FromSeconds(1);

        protected static string PrepareWorkingDirectory(DebuggerStartInfo startInfo)
        {
            var dir = startInfo.WorkingDirectory;
            if (string.IsNullOrEmpty(dir))
                dir = Path.GetDirectoryName(startInfo.Command);
            return dir;
        }

        protected static Dictionary<string, string> PrepareEnvironment(DebuggerStartInfo startInfo)
        {
            Dictionary<string, string> env = new Dictionary<string, string>();
            foreach (DictionaryEntry de in Environment.GetEnvironmentVariables())
                env[(string)de.Key] = (string)de.Value;

            foreach (KeyValuePair<string, string> var in startInfo.EnvironmentVariables)
                env[var.Key] = var.Value;
            return env;
        }

        protected static string PrepareCommandLine(DebuggerStartInfo startInfo)
        {
            // The second parameter of CreateProcess is the command line, and it includes the application being launched
            string cmdLine = "\"" + startInfo.Command + "\" " + startInfo.Arguments;
            return cmdLine;
        }

        protected static void SetupProcess(CorProcess corProcess)
        {
            var processId = corProcess.Id;

            corProcess.OnAssemblyLoad += (sender, e) => 
            {
                Console.WriteLine(e.Assembly.Name);
            };
            /*corProcess.OnCreateProcess += OnCreateProcess;
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
            corProcess.RegisterStdOutput(OnStdOutput);*/
        }

        static void Main(string[] args)
        {
            var startInfo = new DebuggerStartInfo()
            {

                Command = "dotnet.exe",
                Arguments = "c:\\dev\\repos\\dotnettest\\bin\\Debug\\netcoreapp1.1\\dotnettest.dll",
                WorkingDirectory = "c:\\dev\\repos\\dotnettest\\bin\\Debug\\netcoreapp1.1\\",
                UseExternalConsole = true,
                CloseExternalConsoleOnExit = true
            };

            var dbgShimInterop = new DbgShimInterop("C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App\\1.1.1\\dbgshim.dll");

            var workingDir = PrepareWorkingDirectory(startInfo);
            var env = PrepareEnvironment(startInfo);
            var cmd = PrepareCommandLine(startInfo);
            int procId;
            var iCorDebug = CoreClrShimUtil.CreateICorDebugForCommand(
                dbgShimInterop, cmd, workingDir, env, RuntimeLoadTimeout, out procId);
           var  dbg = new CorDebugger(iCorDebug);
            var process = dbg.DebugActiveProcess(procId, false);
            var processId = process.Id;
            SetupProcess(process);
            process.Continue(false);

            

            Console.ReadKey();
        }
    }
}