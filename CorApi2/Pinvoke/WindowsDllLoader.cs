using System;
using System.ComponentModel;
using System.IO;

namespace PinvokeKit
{
    internal unsafe class WindowsDllLoader : IDllLoader
    {
        public IntPtr LoadLibrary(string absoluteDllPath)
        {
            if (!File.Exists(absoluteDllPath))
                throw new ArgumentException("Path is not exists", "absoluteDllPath");

            Kernel32Dll.SetDllDirectoryW(absoluteDllPath);

            try
            {
                var handle = new IntPtr(Kernel32Dll.LoadLibraryW(absoluteDllPath));
                if (handle == IntPtr.Zero)
                    throw new Win32Exception("Unable to load library: " + absoluteDllPath);

                return handle;
            }
            finally
            {
                Kernel32Dll.SetDllDirectoryW(null);
            }
        }

        public void FreeLibrary(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                throw new ArgumentNullException("handle");

            Kernel32Dll.FreeLibrary(handle.ToPointer());
        }

        public IntPtr GetProcAddress(IntPtr handle, string methodName)
        {
            if (handle == IntPtr.Zero)
                throw new ArgumentNullException("handle");

            void* ptr = Kernel32Dll.GetProcAddress(handle.ToPointer(), methodName);
            if (ptr == null)
            {
                var ex = new Win32Exception();

                string path;
                try
                {
                    path = Kernel32Dll.Helpers.GetModulePath(handle.ToPointer());
                }
                catch (Exception)
                {
                    path = string.Empty;
                }

                throw new InvalidOperationException(string.Format("Could not get the {0} entry point from the {1} ({2}) library. {3}", methodName, path.QuoteIfNeeded(), handle.ToHexString(), ex.Message), ex);
            }

            return new IntPtr(ptr);
        }

        public bool IsLoaded(string absoluteDllPath)
        {
            if (File.Exists(absoluteDllPath) )
                throw new ArgumentException("Path is not exists", "absoluteDllPath");

            return Kernel32Dll.GetModuleHandleW(absoluteDllPath) != null;
        }
    }
}