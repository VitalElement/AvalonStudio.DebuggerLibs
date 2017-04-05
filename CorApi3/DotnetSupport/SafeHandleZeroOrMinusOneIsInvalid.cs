using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Win32.SafeHandles
{
    public abstract class SafeHandleZeroOrMinusOneIsInvalid : SafeHandle
    {
        
        protected SafeHandleZeroOrMinusOneIsInvalid(bool ownsHandle) : base(IntPtr.Zero, ownsHandle)
        {
        }


        // A default constructor is needed to satisfy CoreCLR inheritence rules. It should not be called at runtime
        protected SafeHandleZeroOrMinusOneIsInvalid() : base(IntPtr.Zero, true)
        {
            throw new NotImplementedException();
        }

        public override bool IsInvalid
        {
            [System.Security.SecurityCritical]
            get { return handle == IntPtr.Zero || handle == new IntPtr(-1); }
        }
    }

}
