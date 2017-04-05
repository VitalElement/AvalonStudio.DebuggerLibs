using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PinvokeKit
{
    [CLSCompliant(false)]
    public static unsafe class Kernel32Dll
    {
        /// <summary>
        /// Adds a directory to the search path used to locate DLLs for the application.
        /// The <c>SetDllDirectory</c> function affects all subsequent calls to the <c>LoadLibrary</c> and <c>LoadLibraryEx</c> functions. 
        /// It also effectively disables safe DLL search mode while the specified directory is in the search path.
        /// </summary>
        /// <param name="lpPathName">[in, optional] The directory to be added to the search path. 
        /// If this parameter is an empty string (""), the call removes the current directory from the default DLL search order.
        /// If this parameter is NULL, the function restores the default search order.</param>
        /// <returns>If the function succeeds, the return value is nonzero. 
        /// If the function fails, the return value is zero. To get extended error information, call GetLastError. </returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true, ExactSpelling = true)]
        public static extern Int32 SetDllDirectoryW(string lpPathName);

        /// <summary>
        /// The LoadLibrary function maps the specified executable module into the address space of the calling process.
        /// For additional load options, use the LoadLibraryEx function.
        /// </summary>
        /// <param name="lpFileName">[in] Pointer to a null-terminated string that names the executable module (either a .dll or .exe file). The name specified is the file name of the module and is not related to the name stored in the library module itself, as specified by the LIBRARY keyword in the module-definition (.def) file.
        /// If the string specifies a path but the file does not exist in the specified directory, the function fails. When specifying a path, be sure to use backslashes (\), not forward slashes (/).
        /// If the string does not specify a path, the function uses a standard search strategy to find the file. See the Remarks for more information.</param>
        /// <returns>If the function succeeds, the return value is a handle to the module.
        /// If the function fails, the return value is NULL. To get extended error information, call GetLastError.
        /// Windows Me/98/95:  If you are using LoadLibrary to load a module that contains a resource whose numeric identifier is greater than 0x7FFF, LoadLibrary fails.
        /// If you are attempting to load a 16-bit DLL directly from 32-bit code, LoadLibrary fails. If you are attempting to load a DLL whose subsystem version is greater than 4.0, LoadLibrary fails. If your DllMain function tries to call the Unicode version of a function, LoadLibrary fails.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true, ExactSpelling = true)]
        public static extern void* LoadLibraryW(string lpFileName);

        /// <summary>
        /// Retrieves the address of an exported function or variable from the specified dynamic-link library (DLL).
        /// </summary>
        /// <param name="hModule">A handle to the DLL module that contains the function or variable. The LoadLibrary or GetModuleHandle function returns this handle.</param>
        /// <param name="lpProcName">The function or variable name, or the function's ordinal value. If this parameter is an ordinal value, it must be in the low-order word; the high-order word must be zero.</param>
        /// <returns>If the function succeeds, the return value is the address of the exported function or variable. If the function fails, the return value is NULL. To get extended error information, call GetLastError.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true, ExactSpelling = true)]
        public static extern void* GetProcAddress(void* hModule, [MarshalAs(UnmanagedType.LPStr)] string lpProcName);

        /// <summary>
        /// Frees the loaded dynamic-link library (DLL) module and, if necessary, decrements its reference count.
        /// When the reference count reaches zero, the module is unloaded from the address space of the calling process and the handle is no longer valid.
        /// </summary>
        /// <param name="hModule">[in] A handle to the loaded library module.
        /// The LoadLibrary, LoadLibraryEx, GetModuleHandle, or GetModuleHandleEx function returns this handle.</param>
        /// <returns>If the function succeeds, the return value is nonzero.
        /// If the function fails, the return value is zero. To get extended error information, call the GetLastError function.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true, ExactSpelling = true)]
        public static extern Int32 FreeLibrary(void* hModule);

        /// <summary>
        /// Retrieves the fully-qualified path for the file that contains the specified module. The module must have been loaded by the current process.
        /// To locate the file for a module that was loaded by another process, use the GetModuleFileNameEx function.
        /// </summary>
        /// <param name="hModule">
        /// A handle to the loaded module whose path is being requested. If this parameter is NULL, GetModuleFileName retrieves the path of the executable file of the current process.
        /// The GetModuleFileName function does not retrieve the path for modules that were loaded using the LOAD_LIBRARY_AS_DATAFILE flag. For more information, see LoadLibraryEx.
        /// </param>
        /// <param name="lpFilename">
        /// A pointer to a buffer that receives the fully-qualified path of the module. If the length of the path is less than the size that the nSize parameter specifies, the function succeeds and the path is returned as a null-terminated string.
        /// If the length of the path exceeds the size that the nSize parameter specifies, the function succeeds and the string is truncated to nSize characters including the terminating null character.
        /// Windows XP/2000:  The string is truncated to nSize characters and is not null terminated.
        /// The string returned will use the same format that was specified when the module was loaded. Therefore, the path can be a long or short file name, and can use the prefix "\\?\". For more information, see Naming a File.
        /// </param>
        /// <param name="nSize">The size of the lpFilename buffer, in TCHARs.</param>
        /// <returns>
        /// If the function succeeds, the return value is the length of the string that is copied to the buffer, in characters, not including the terminating null character. If the buffer is too small to hold the module name, the string is truncated to nSize characters including the terminating null character, the function returns nSize, and the function sets the last error to ERROR_INSUFFICIENT_BUFFER.
        /// Windows XP/2000:  If the buffer is too small to hold the module name, the function returns nSize. The last error code remains ERROR_SUCCESS. If nSize is zero, the return value is zero and the last error code is ERROR_SUCCESS.
        /// If the function fails, the return value is 0 (zero). To get extended error information, call GetLastError.
        /// </returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        public static extern UInt32 GetModuleFileNameW(void* hModule, char* lpFilename, UInt32 nSize);

        /// <summary>
        /// Retrieves a module handle for the specified module. The module must have been loaded by the calling process.
        /// To avoid the race conditions described in the Remarks section, use the GetModuleHandleEx function.
        /// </summary>
        /// <param name="lpModuleName">The name of the loaded module (either a .dll or .exe file).
        /// If the file name extension is omitted, the default library extension .dll is appended.
        /// The file name string can include a trailing point character (.) to indicate that the module name has no extension.
        /// The string does not have to specify a path. When specifying a path, be sure to use backslashes (\), not forward slashes (/).
        /// The name is compared (case independently) to the names of modules currently mapped into the address space of the calling process.
        /// If this parameter is NULL, GetModuleHandle returns a handle to the file used to create the calling process (.exe file).
        /// The GetModuleHandle function does not retrieve handles for modules that were loaded using the LOAD_LIBRARY_AS_DATAFILE flag.
        /// For more information, see LoadLibraryEx.</param>
        /// <returns>If the function succeeds, the return value is a handle to the specified module.
        /// If the function fails, the return value is NULL. To get extended error information, call GetLastError.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true, ExactSpelling = true)]
        public static extern void* GetModuleHandleW(string lpModuleName);

        /// <summary>
        /// Retrieves information about the current system. To retrieve accurate information for an application running on WOW64, call the GetNativeSystemInfo function.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true, ExactSpelling = true)]
        public static extern void GetSystemInfo(SYSTEM_INFO* lpSystemInfo);

        /// <summary>
        /// Retrieves information about the current system to an application running under WOW64. If the function is called from a 64-bit application, it is equivalent to the GetSystemInfo function.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true, ExactSpelling = true)]
        public static extern void GetNativeSystemInfo(SYSTEM_INFO* lpSystemInfo);

        /// <summary>Opens an existing local process object.</summary>
        /// <param name="dwDesiredAccess">The access to the process object.</param>
        /// <param name="bInheritHandle">If this value is TRUE, processes created by this process will inherit the handle.</param>
        /// <param name="dwProcessId">The identifier of the local process to be opened. If the specified process is the System Process (0x00000000), the function fails and the last error code is ERROR_INVALID_PARAMETER. If the specified process is the Idle process or one of the CSRSS processes, this function fails and the last error code is ERROR_ACCESS_DENIED because their access restrictions prevent user-level code from opening them.</param>
        /// <returns>If the function succeeds, the return value is an open handle to the specified process. If the function fails, the return value is NULL. To get extended error information, call GetLastError.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true, ExactSpelling = true)]
        public static extern void* OpenProcess(UInt32 dwDesiredAccess, Int32 bInheritHandle, UInt32 dwProcessId);

        /// <summary>
        /// Wrappers for the functions in this DLL.
        /// </summary>
        public static class Helpers
        {
            /// <summary>
            /// Retrieves the fully qualified path for the file that contains the specified module. The module must have been loaded by the current process.
            /// Wraps WinAPI function <see cref="GetModuleFileNameW"/>.
            /// </summary>
            /// <param name="hModule">A handle to the loaded module whose path is being requested.
            /// If this parameter is NULL, GetModuleFileName retrieves the path of the executable file of the current process.</param>
            /// <returns>The fully qualified path of the module.</returns>
            public static string GetModulePath(void* hModule)
            {
                char* szPath = stackalloc char[WinDef.MAX_PATH];
                uint nActualLengthWithoutZero;
                if ((nActualLengthWithoutZero = GetModuleFileNameW(hModule, szPath, WinDef.MAX_PATH)) == 0)
                {
                    var exInner = new Win32Exception();
                    throw new InvalidOperationException("Could not get the file name of the module in the current process.", exInner);
                }
                nActualLengthWithoutZero = nActualLengthWithoutZero < WinDef.MAX_PATH ? nActualLengthWithoutZero : WinDef.MAX_PATH - 1;
                szPath[nActualLengthWithoutZero] = (char)0; // Ensure zero-terminated, on XP this is not guaranteed if the path is long enough

                return new string(szPath);
            }
        }

        static class WinDef
        {
            public const int MAX_PATH = 260;
        }
    }
}