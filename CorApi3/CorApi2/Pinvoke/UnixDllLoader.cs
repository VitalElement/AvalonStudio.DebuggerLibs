using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PinvokeKit
{
    internal class UnixDllLoader : IDllLoader
    {
        // ReSharper disable once InconsistentNaming
        private const int RTLD_NOW = 2;

        // ReSharper disable once InconsistentNaming
        private const int RTLD_NOLOAD = 0x10;

        public IntPtr LoadLibrary(string absoluteDllPath)
        {
            if (File.Exists(absoluteDllPath) )
                throw new ArgumentException("Path is not exists", "absoluteDllPath");

            ResetLastError();

            var handle = dlopen(absoluteDllPath, RTLD_NOW);
            if (handle == IntPtr.Zero)
                ThrowError("dlopen");

            return handle;
        }

        public void FreeLibrary(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                throw new ArgumentNullException("handle");

            ResetLastError();

            if (dlclose(handle) != 0)
                ThrowError("dlclose: unable to close dynamic library");
        }

        public IntPtr GetProcAddress(IntPtr handle, string methodName)
        {
            if (handle == IntPtr.Zero)
                throw new ArgumentNullException("handle");

            ResetLastError();

            var res = dlsym(handle, methodName);
            if (res == IntPtr.Zero)
                ThrowError("dlsym: unable to get symbol " + methodName); // TODO [shalupov]: print path to the library too

            return res;
        }

        public bool IsLoaded(string absoluteDllPath)
        {
            IntPtr handle = dlopen(absoluteDllPath, RTLD_NOLOAD);
            if (handle != IntPtr.Zero)
                dlclose(handle);

            return handle != IntPtr.Zero;
        }

        private static void ThrowError(string message)
        {
            var errPtr = dlerror();
            throw new Exception(message + ": " + (errPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errPtr) : ""));
        }

        private static void ResetLastError()
        {
            // Reset last error
            dlerror();
        }

        private static IntPtr dlopen(string fileName, int flags)
        {
            try
            {
                return LibDlSo2.dlopen(fileName, flags);
            }
            catch (DllNotFoundException)
            {
                return LibDl.dlopen(fileName, flags);
            }
        }

        private static IntPtr dlsym(IntPtr handle, string symbol)
        {
            try
            {
                return LibDlSo2.dlsym(handle, symbol);
            }
            catch (DllNotFoundException)
            {
                return LibDl.dlsym(handle, symbol);
            }
        }

        private static int dlclose(IntPtr handle)
        {
            try
            {
                return LibDlSo2.dlclose(handle);
            }
            catch (DllNotFoundException)
            {
                return LibDl.dlclose(handle);
            }
        }

        private static IntPtr dlerror()
        {
            try
            {
                return LibDlSo2.dlerror();
            }
            catch (DllNotFoundException)
            {
                return LibDl.dlerror();
            }
        }

        private static class LibDl
        {
            [DllImport("libdl")]
            public static extern IntPtr dlopen(string fileName, int flags);

            [DllImport("libdl")]
            public static extern IntPtr dlsym(IntPtr handle, string symbol);

            [DllImport("libdl")]
            public static extern int dlclose(IntPtr handle);

            [DllImport("libdl")]
            public static extern IntPtr dlerror();
        }

        private static class LibDlSo2
        {
            [DllImport("libdl.so.2")]
            public static extern IntPtr dlopen(string fileName, int flags);

            [DllImport("libdl.so.2")]
            public static extern IntPtr dlsym(IntPtr handle, string symbol);

            [DllImport("libdl.so.2")]
            public static extern int dlclose(IntPtr handle);

            [DllImport("libdl.so.2")]
            public static extern IntPtr dlerror();
        }
    }
}