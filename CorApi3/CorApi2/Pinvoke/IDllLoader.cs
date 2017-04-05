using System;

namespace PinvokeKit
{
    public interface IDllLoader
    {
        IntPtr LoadLibrary(string absoluteDllPath);
        void FreeLibrary(IntPtr handle);
        IntPtr GetProcAddress(IntPtr handle, string methodName);

        bool IsLoaded(string absoluteDllPath);
    }
}