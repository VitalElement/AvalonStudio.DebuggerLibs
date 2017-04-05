using System;
using System.Runtime.InteropServices;
using PinvokeKit;

namespace Microsoft.Samples.Debugging.CorDebug
{
    public unsafe class DbgShimInterop
    {
        public DbgShimInterop (string dbgShimPath)
        {
            var dll = NativeDllsLoader.LoadDll(dbgShimPath);
            CreateProcessForLaunch = dll.ImportMethod<CreateProcessForLaunchDelegate> ("CreateProcessForLaunch");
            RegisterForRuntimeStartup = dll.ImportMethod<RegisterForRuntimeStartupDelegate>("RegisterForRuntimeStartup");
            ResumeProcess = dll.ImportMethod<ResumeProcessDelegate>("ResumeProcess");
            CloseResumeHandle = dll.ImportMethod<CloseResumeHandleDelegate>("CloseResumeHandle");
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate Int32 CreateProcessForLaunchDelegate(
            [In, MarshalAsAttribute(UnmanagedType.LPWStr)]string lpCommandLine,
            [In]bool bSuspendProcess,
            [In]IntPtr lpEnvironment,
            [In, MarshalAsAttribute(UnmanagedType.LPWStr)]string lpCurrentDirectory,
            [Out]UInt32* pProcessId,
            [Out]void** pResumeHandle);
        public readonly CreateProcessForLaunchDelegate CreateProcessForLaunch;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void RuntimeStartupCallback([In]void* pCordb, [In]void* parameter, [Out]Int32 hr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate Int32 RegisterForRuntimeStartupDelegate(
            [In]UInt32 dwProcessId,
            [In]IntPtr callback,
            [In]void* parameter,
            [In]void** ppUnregisterToken);
        public readonly RegisterForRuntimeStartupDelegate RegisterForRuntimeStartup;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate Int32 ResumeProcessDelegate([In]void* hResumeHandle);
        public readonly ResumeProcessDelegate ResumeProcess;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate Int32 CloseResumeHandleDelegate([In]void* hResumeHandle);
        public readonly CloseResumeHandleDelegate CloseResumeHandle;
    }
}