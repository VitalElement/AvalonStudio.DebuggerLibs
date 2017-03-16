using System;
using System.Runtime.InteropServices;

namespace Microsoft.Samples.Debugging.CorDebug
{
    internal static class NativeMethodsShim
    {
        const string DbgshimDll = "dbgshim";

        [DllImport(DbgshimDll, CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern unsafe void CreateProcessForLaunch(
            string lpCommandLine,
            bool bSuspendProcess,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            UInt32* pProcessId,
            void** pResumeHandle);

        [DllImport(DbgshimDll, CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern unsafe void ResumeProcess(
            void* hResumeHandle);

        [DllImport(DbgshimDll, CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern unsafe void CloseResumeHandle(
            void* hResumeHandle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate void RuntimeStartupCallback(void* pCordb, void* parameter, Int32 hr);

        [DllImport(DbgshimDll, CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern unsafe void RegisterForRuntimeStartup(
            uint dwProcessId,
            IntPtr callback,
            void* parameter,
            void** ppUnregisterToken);
    }
}