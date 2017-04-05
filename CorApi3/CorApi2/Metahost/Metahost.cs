using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Samples.Debugging.CorDebug;

namespace Microsoft.Samples.Debugging.CorPublish.Metahost
{
    [ComImport]
    [SecurityCritical]
    [Guid ("00000100-0000-0000-C000-000000000046")]
    [InterfaceType (ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumUnknown
    {
        [PreserveSig]
       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        int Next (
            [In, MarshalAs (UnmanagedType.U4)] int elementArrayLength,
            [Out, MarshalAs (UnmanagedType.LPArray, ArraySubType = UnmanagedType.IUnknown, SizeParamIndex = 0)] object[] elementArray,
            [MarshalAs (UnmanagedType.U4)] out int fetchedElementCount);

        [PreserveSig]
       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        int Skip ([In, MarshalAs (UnmanagedType.U4)] int count);

       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        void Reset ();

       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        void Clone ([MarshalAs (UnmanagedType.Interface)] out IEnumUnknown enumerator);
    }

    [SecurityCritical]
    [InterfaceType (ComInterfaceType.InterfaceIsIUnknown)]
    [Guid ("D332DB9E-B9B3-4125-8207-A14884F53216")]
    internal interface IClrMetaHost
    {
        [return: MarshalAs (UnmanagedType.Interface)]
       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        object GetRuntime (
            [In, MarshalAs (UnmanagedType.LPWStr)] string version,
            [In, MarshalAs (UnmanagedType.LPStruct)] Guid interfaceId);

       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        void GetVersionFromFile (
            [In, MarshalAs (UnmanagedType.LPWStr)] string filePath,
            [Out, MarshalAs (UnmanagedType.LPWStr)] StringBuilder buffer,
            [In, Out, MarshalAs (UnmanagedType.U4)] ref uint bufferLength);

        [return: MarshalAs (UnmanagedType.Interface)]
       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        IEnumUnknown EnumerateInstalledRuntimes ();

        [return: MarshalAs (UnmanagedType.Interface)]
        //[MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        IEnumUnknown EnumerateLoadedRuntimes ([In] ProcessSafeHandle processHandle);

        [PreserveSig, MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        int Reserved01 ([In] IntPtr reserved1);
    }

    [System.Security.SecurityCritical]
    [ComImport, InterfaceType (ComInterfaceType.InterfaceIsIUnknown), Guid ("BD39D1D2-BA2F-486A-89B0-B4B0CB466891")]
    internal interface IClrRuntimeInfo
    {
        [PreserveSig]
        int GetVersionString (
            [Out, MarshalAs (UnmanagedType.LPWStr, SizeParamIndex = 1)] StringBuilder buffer,
            [In, Out, MarshalAs (UnmanagedType.U4)] ref int bufferLength);

       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        [PreserveSig]
        int GetRuntimeDirectory (
            [Out, MarshalAs (UnmanagedType.LPWStr, SizeParamIndex = 1)] StringBuilder buffer,
            [In, Out, MarshalAs (UnmanagedType.U4)] ref int bufferLength);

        [return: MarshalAs (UnmanagedType.Bool)]
       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        bool IsLoaded (
            [In] IntPtr processHandle);

        [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        [PreserveSig]
        int LoadErrorString (
            [In, MarshalAs (UnmanagedType.U4)] int resourceId,
            [Out, MarshalAs (UnmanagedType.LPWStr, SizeParamIndex = 2)] StringBuilder buffer,
            [In, Out, MarshalAs (UnmanagedType.U4)] ref int bufferLength);

        //@TODO: SafeHandle?
       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        IntPtr LoadLibrary (
            [In, MarshalAs (UnmanagedType.LPWStr)] string dllName);

       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        IntPtr GetProcAddress (
            [In, MarshalAs (UnmanagedType.LPStr)] string procName);

        [return: MarshalAs (UnmanagedType.Interface)]
       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        object GetInterface (
            [In, MarshalAs (UnmanagedType.LPStruct)] Guid coClassId,
            [In, MarshalAs (UnmanagedType.LPStruct)] Guid interfaceId);

        [return: MarshalAs (UnmanagedType.Bool)]
       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        bool IsLoadable ();

       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        void SetDefaultStartupFlags (
            [In, MarshalAs (UnmanagedType.U4)] StartupFlags startupFlags,
            [In, MarshalAs (UnmanagedType.LPStr)] string hostConfigFile);

       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        [PreserveSig]
        int GetDefaultStartupFlags (
            [Out, MarshalAs (UnmanagedType.U4)] out StartupFlags startupFlags,
            [Out, MarshalAs (UnmanagedType.LPWStr, SizeParamIndex = 2)] StringBuilder hostConfigFile,
            [In, Out, MarshalAs (UnmanagedType.U4)] ref int hostConfigFileLength);

       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        void BindAsLegacyV2Runtime ();

       [MethodImpl((MethodImplOptions)System.Reflection.MethodImplAttributes.InternalCall)]
        void IsStarted (
            [Out, MarshalAs (UnmanagedType.Bool)] out bool started,
            [Out, MarshalAs (UnmanagedType.U4)] out StartupFlags startupFlags);
    }

    [Flags]
    [System.Security.SecurityCritical]
    public enum StartupFlags
    {
        None = 0,
        AlwaysFlowImpersonation = 0x40000,
        AppdomainResourceMonitoring = 0x400000, // Appdomain Resource Monitoring feature
        ConcurrentGC = 1,
        DisableCommitThreadStack = 0x20000,

        // Abbreviation fine here; it's not hungarian notation!
        [SuppressMessage ("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly")] Etw = 0x100000,

        // Wants "GC" and "VM" rather than "Gc" and "Vm" but then complains that "GCVM" should be "Gcvm".
        [SuppressMessage ("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly")] HoardGCVM = 0x2000,
        LegacyImpersonation = 0x10000,
        LoaderOptimizationMask = 6,
        LoaderOptimizationMultipleDomain = 4,
        LoaderOptimizationMultipleDomainHost = 6,
        LoaderOptimizationSingleDomain = 2,
        LoaderSafeMode = 0x10,
        LoaderSetPreference = 0x100,
        ServerBuild = 0x200000,
        ServerGC = 0x1000,
        SingleVersionHostingInterface = 0x4000,
        TrimGCCommit = 0x80000
    }
}