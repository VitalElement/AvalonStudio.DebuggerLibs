using System.Runtime.InteropServices;

namespace System.Diagnostics
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false)]
    [ComVisible(true)]
    public sealed class DebuggerStepperBoundaryAttribute : Attribute
    {
        public DebuggerStepperBoundaryAttribute() { }
    }
}