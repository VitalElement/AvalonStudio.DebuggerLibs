using System;
using System.Runtime.InteropServices;

namespace Microsoft.Samples.Debugging.CorDebug
{
    public static class HResultUtil
    {
        public static THResult? ToHResult<THResult> (this ExternalException ex) where THResult : struct
        {
            if (Enum.IsDefined (typeof(THResult), ex.ErrorCode)) {
                return (THResult?) Enum.ToObject (typeof(THResult), ex.ErrorCode);
            }
            return null;
        }

        public static THResult? ToHResult<THResult> (this SharpGen.Runtime.SharpGenException ex) where THResult : struct
        {
            if (Enum.IsDefined (typeof (THResult), ex.HResult)) {
                return (THResult?)Enum.ToObject (typeof (THResult), ex.HResult);
            }
            return null;
        }
    }
}