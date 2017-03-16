using System;
using System.Runtime.InteropServices;

namespace PinvokeKit
{
    [CLSCompliant(false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_INFO
    {
        /// <seealso cref="ProcessorArchitecture"/>
        public UInt16 wProcessorArchitecture;
        public UInt16 wReserved;
        public UInt32 dwPageSize;
        public IntPtr lpMinimumApplicationAddress;
        public IntPtr lpMaximumApplicationAddress;
        public UIntPtr dwActiveProcessorMask;
        public UInt32 dwNumberOfProcessors;
        public UInt32 dwProcessorType;
        public UInt32 dwAllocationGranularity;
        public UInt16 wProcessorLevel;
        public UInt16 wProcessorRevision;
    }
}