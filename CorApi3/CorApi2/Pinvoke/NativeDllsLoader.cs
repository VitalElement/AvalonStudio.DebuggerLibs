using System;
using System.Collections.Generic;
using System.IO;

namespace PinvokeKit
{
    /// <summary>
    ///   Helps to load and work with native DLL modules.
    /// </summary>
    /// <remarks>
    ///   Calling <c>LoadLibrary</c> increments the reference count.
    ///   Calling the <c>FreeLibrary</c> or <c>FreeLibraryAndExitThread</c> function decrements the reference count.
    ///   The system unloads a module when its reference count reaches zero or when the process terminates (regardless of the
    ///   reference count).
    /// </remarks>
    public static unsafe class NativeDllsLoader
    {
        private static readonly IDllLoader ourDllLoader = PlatformUtil.IsRunningUnderWindows ? (IDllLoader) new WindowsDllLoader() : new UnixDllLoader();

        /// <summary>
        ///   Loads the DLL module and returns an instance of <see cref="NativeDll" /> class.
        /// </summary>
        /// <param name="path">The absolute path of a DLL to load.</param>
        public static NativeDll LoadDll(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException("path");

            var file = AddDynamicLibrarySuffix(StripExistingDynamicLibrarySuffix(path));

            if (!File.Exists(file))
                throw new ArgumentException(string.Format("Dynamic library {0} is not found by path {1}", file.QuoteIfNeeded(), file.QuoteIfNeeded()));

            var handle = ourDllLoader.LoadLibrary(file);
            return new NativeDll(handle.ToPointer(), file, ourDllLoader);
        }

        private static readonly string WindowsDynamicLibrarySuffix = ".dll";
        private static readonly string LinuxDynamicLibrarySuffix = ".so";
        private static readonly string MacOsDynamicLibrarySuffix = ".dylib";

        private static readonly List<string> DynamicLibrariesSuffixes = new List<string>
        {
            WindowsDynamicLibrarySuffix,
            LinuxDynamicLibrarySuffix,
            MacOsDynamicLibrarySuffix,
        };

        private static string StripExistingDynamicLibrarySuffix(string relativePath)
        {
            var path = relativePath;

            foreach (var suffix in DynamicLibrariesSuffixes)
            {
                if (path.ToLowerInvariant().EndsWith(suffix.ToLowerInvariant()))
                    return path.Substring(0, path.Length - suffix.Length);
            }

            return relativePath;
        }

        private static string AddDynamicLibrarySuffix(string relativePath)
        {
            return relativePath + GetSuffix();
        }

        private static string GetSuffix()
        {
            switch (PlatformUtil.RuntimePlatform)
            {
                case PlatformUtil.Platform.Windows:
                    return WindowsDynamicLibrarySuffix;
                case PlatformUtil.Platform.MacOsX:
                    return MacOsDynamicLibrarySuffix;
                case PlatformUtil.Platform.Linux:
                    return LinuxDynamicLibrarySuffix;
            }

            throw new Exception("Unsupported runtime platform: " + PlatformUtil.RuntimePlatform);
        }
    }
}