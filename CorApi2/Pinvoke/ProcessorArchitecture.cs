using System;

namespace PinvokeKit
{
    [CLSCompliant(false)]
    public enum ProcessorArchitecture : ushort
    {
        /// <summary>
        /// x86
        /// </summary>
        PROCESSOR_ARCHITECTURE_INTEL = 0,
        PROCESSOR_ARCHITECTURE_MIPS = 1,
        PROCESSOR_ARCHITECTURE_ALPHA = 2,
        PROCESSOR_ARCHITECTURE_PPC = 3,
        PROCESSOR_ARCHITECTURE_SHX = 4,
        PROCESSOR_ARCHITECTURE_ARM = 5,
        /// <summary>
        /// Intel Itanium-based
        /// </summary>
        PROCESSOR_ARCHITECTURE_IA64 = 6,
        PROCESSOR_ARCHITECTURE_ALPHA64 = 7,
        PROCESSOR_ARCHITECTURE_MSIL = 8,
        /// <summary>
        /// x64 (AMD or Intel)
        /// </summary>
        PROCESSOR_ARCHITECTURE_AMD64 = 9,
        PROCESSOR_ARCHITECTURE_IA32_ON_WIN64 = 10,
        PROCESSOR_ARCHITECTURE_NEUTRAL = 11,
        /// <summary>
        /// Unknown architecture.
        /// </summary>
        PROCESSOR_ARCHITECTURE_UNKNOWN = 0xFFFF,
    }
}