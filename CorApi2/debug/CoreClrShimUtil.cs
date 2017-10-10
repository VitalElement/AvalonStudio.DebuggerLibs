using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Samples.Debugging.CorDebug.NativeApi;
using Microsoft.Samples.Debugging.Extensions;
using PinvokeKit;

namespace Microsoft.Samples.Debugging.CorDebug
{
    public static class CoreClrShimUtil
    {
        [CLSCompliant (false)]
        public static ICorDebug CreateICorDebugForCommand (DbgShimInterop dbgShimInterop, string command, string workingDir,
            IDictionary<string, string> env, TimeSpan runtimeLoadTimeout, out int procId)
        {
            unsafe {
                IntPtr envPtr = IntPtr.Zero;
                try {
                    envPtr = DebuggerExtensions.SetupEnvironment (env);
                    void* resumeHandle;
                    uint processId;
                    var hret =  (HResults)dbgShimInterop.CreateProcessForLaunch (command, true, envPtr, workingDir, &processId, &resumeHandle);
                    if (hret != HResults.S_OK)
                        throw new COMException(string.Format ("Failed call RegisterForRuntimeStartup: {0}", hret), (int)hret);
                    procId = (int) processId;
                    return CreateICorDebugImpl (dbgShimInterop, processId, runtimeLoadTimeout, resumeHandle);
                } finally {
                    if (envPtr != IntPtr.Zero )
                        DebuggerExtensions.TearDownEnvironment (envPtr);
                }
            }
        }

        [CLSCompliant(false)]
        public static CorApi.Portable.LocalDebugger CreateCorDebugForCommand(DbgShimInterop dbgShimInterop, string command, string workingDir,
            IDictionary<string, string> env, TimeSpan runtimeLoadTimeout, Action<CorApi.Portable.LocalDebugger, int> onSetup, out int procId)
        {
            unsafe
            {
                IntPtr envPtr = IntPtr.Zero;
                try
                {
                    envPtr = DebuggerExtensions.SetupEnvironment(env);
                    void* resumeHandle;
                    uint processId;
                    var hret = (HResults)dbgShimInterop.CreateProcessForLaunch(command, true, envPtr, workingDir, &processId, &resumeHandle);
                    if (hret != HResults.S_OK)
                        throw new COMException(string.Format("Failed call RegisterForRuntimeStartup: {0}", hret), (int)hret);
                    procId = (int)processId;
                    return CreateCorDebugImpl(dbgShimInterop, processId, runtimeLoadTimeout, resumeHandle, onSetup);
                }
                finally
                {
                    if (envPtr != IntPtr.Zero)
                        DebuggerExtensions.TearDownEnvironment(envPtr);
                }
            }
        }



        [CLSCompliant(false)]
        public static ICorDebug CreateICorDebugForProcess (DbgShimInterop dbgShimInterop, int processId, TimeSpan runtimeLoadTimeout)
        {
            unsafe {
                return CreateICorDebugImpl (dbgShimInterop, (uint) processId, runtimeLoadTimeout, null);
            }
        }

        private static unsafe CorApi.Portable.LocalDebugger CreateCorDebugImpl(DbgShimInterop dbgShimInterop, uint processId, TimeSpan runtimeLoadTimeout, void* resumeHandle, Action<CorApi.Portable.LocalDebugger, int> onSetup)
        {
            var waiter = new ManualResetEvent(false);
            CorApi.Portable.LocalDebugger corDebug = null;
            Exception callbackException = null;
            void* token;
            DbgShimInterop.RuntimeStartupCallback callback = delegate (void* pCordb, void* parameter, int hr) {
                try
                {
                    if (hr < 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }
                    
                    corDebug = SharpDX.ComObject.FromPointer<CorApi.Portable.LocalDebugger>((IntPtr)pCordb);

                    onSetup(corDebug, (int)processId);
                    
                }
                catch (Exception e)
                {
                    callbackException = e;
                }
                waiter.Set();
            };
            var callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);

            var hret = (HResults)dbgShimInterop.RegisterForRuntimeStartup(processId, callbackPtr, null, &token);

            if (hret != HResults.S_OK)
                throw new COMException(string.Format("Failed call RegisterForRuntimeStartup: {0}", hret), (int)hret);

            if (resumeHandle != null)
                dbgShimInterop.ResumeProcess(resumeHandle);

            if (!waiter.WaitOne(runtimeLoadTimeout))
            {
                throw new TimeoutException(string.Format(".NET core load awaiting timed out for {0}", runtimeLoadTimeout));
            }

            GC.KeepAlive(callback);
            if (callbackException != null)
                throw callbackException;
            return corDebug;
        }

        private static unsafe ICorDebug CreateICorDebugImpl (DbgShimInterop dbgShimInterop, uint processId, TimeSpan runtimeLoadTimeout, void* resumeHandle)
        {
            var waiter = new ManualResetEvent (false);
            ICorDebug corDebug = null;
            Exception callbackException = null;
            void* token;
            DbgShimInterop.RuntimeStartupCallback callback = delegate (void* pCordb, void* parameter, int hr) {
                try {
                    if (hr < 0) {
                        Marshal.ThrowExceptionForHR (hr);
                    }

                    var debugger = SharpDX.ComObject.FromPointer<CorApi.Portable.LocalDebugger>((IntPtr)pCordb);

                    var unknown = Marshal.GetObjectForIUnknown ((IntPtr) pCordb);
                    corDebug = (ICorDebug) unknown;
                } catch (Exception e) {
                    callbackException = e;
                }
                waiter.Set ();
            };
            var callbackPtr = Marshal.GetFunctionPointerForDelegate (callback);

            var hret =  (HResults)dbgShimInterop.RegisterForRuntimeStartup (processId, callbackPtr, null, &token);

            if (hret != HResults.S_OK)
                throw new COMException(string.Format ("Failed call RegisterForRuntimeStartup: {0}", hret), (int)hret);

            if (resumeHandle != null)
                dbgShimInterop.ResumeProcess (resumeHandle);

            if (!waiter.WaitOne (runtimeLoadTimeout)) {
                throw new TimeoutException (string.Format (".NET core load awaiting timed out for {0}", runtimeLoadTimeout));
            }
            GC.KeepAlive (callback);
            if (callbackException != null)
                throw callbackException;
            return corDebug;
        }

    }
}
