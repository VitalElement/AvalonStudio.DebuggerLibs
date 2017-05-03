using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PinvokeKit
{
    /// <summary>
    /// Encapsulates a native DLL module. Use <see cref="NativeDllsLoader"/> to get instances of this class.
    /// </summary>
    public unsafe sealed class NativeDll
    {
        /// <summary>
        /// The DLL module handle.
        /// </summary>
        internal readonly void* Handle;

        private readonly IDllLoader DllLoader;

        /// <summary>
        /// The absolute path to the DLL file on disk.
        /// </summary>
        internal readonly string File;

        /// <summary>
        /// Internal constructor. Used by <see cref="NativeDllsLoader"/>.
        /// </summary>
        internal NativeDll( void* handle,  string file, IDllLoader dllLoader)
        {
            if (handle == null)
                throw new ArgumentNullException("handle");
            if (dllLoader == null)
                throw new ArgumentNullException("dllLoader");
            if (string.IsNullOrEmpty(file))
                throw new ArgumentNullException("file");
            if (!System.IO.File.Exists(file))
                throw new ArgumentOutOfRangeException("file");

            Handle = handle;
            File = file;
            DllLoader = dllLoader;
        }

        /// <summary>
        /// The table of loaded DLL entry points.
        /// </summary>
        private readonly Dictionary<string, Delegate> myMethods = new Dictionary<string, Delegate>();

        /// <summary>
        /// Gets a delegate instance for the specified DLL entry point.
        /// </summary>
        /// <typeparam name="TDelegate">The delegate type a DLL entry point to be converted to.</typeparam>
        /// <param name="methodName">The name of a DLL entry point.</param>
        public TDelegate ImportMethod<TDelegate>(string methodName) where TDelegate : class
        {
            if (methodName == null)
                throw new ArgumentNullException("methodName");

            Delegate deleg;
            if (!myMethods.TryGetValue(methodName, out deleg))
            {
                var procAddress = DllLoader.GetProcAddress(new IntPtr(Handle), methodName);
                deleg = Marshal.GetDelegateForFunctionPointer(procAddress, typeof(TDelegate));

                myMethods.Add(methodName, deleg);
            }

            // Ideally, we'd just make the constraint on TDelegate be System.Delegate,
            // but compiler error CS0702 (constrained can't be System.Delegate) prevents that.
            // So we make the constraint system.object and do the cast to TDelegate.
            object obj = deleg;
            return (TDelegate)obj;
        }

        /// <summary>
        /// Creates an instance of a COM object without the Registry information, by invoking its class factory.
        /// </summary>
        /// <param name="guidClsid">CLSID of the object to create.</param>
        public ComObject CreateComObject(Guid guidClsid)
        {
            // Get factory provider entry point
            var funcDllGetClassObject = ImportMethod<DllGetClassObjectDelegate>("DllGetClassObject");

            object instance;
            try
            {
                #if NETSTANDARD2_0
                throw new NotImplementedException("Not available in notstandard");
#else
                
                // Get the factory
                IClassFactory factory;

                
                Guid iidClassFactory = Marshal.GenerateGuidForType(typeof(IClassFactory));
                int retval = funcDllGetClassObject(&guidClsid, &iidClassFactory, out factory);
                if (retval < 0)
                    Marshal.ThrowExceptionForHR(retval);

                // Make the factory create the object
                var iidIUnknown = new Guid("00000000-0000-0000-C000-000000000046");
                factory.CreateInstance(null, iidIUnknown, out instance);
#endif
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(String.Format("Could not create an instance of {0} from {1}. {2}", guidClsid, File.QuoteIfNeeded(), ex.Message), ex);
            }

            if (instance == null)
                throw new InvalidOperationException(String.Format("Failed to create an instance of {0} from {1}.", guidClsid, File.QuoteIfNeeded()));

            return new ComObject(instance, guidClsid);
        }

        /// <summary>
        /// Creates an instance of a COM object without the Registry information, by invoking its class factory.
        /// </summary>
        /// <typeparam name="TType">The type of the object to create.</typeparam>
        public ComObject CreateComObject<TType>() where TType : class
        {
            return CreateComObject(typeof(TType).GUID);
        }

        /// <summary>
        /// A delegate for the DllGetClassObject function.
        /// </summary>
        private delegate Int32 DllGetClassObjectDelegate(Guid* rclsid, Guid* riid, [MarshalAs(UnmanagedType.Interface)] [Out] out IClassFactory ppv);

        /// <summary>
        /// A wrapper for an instance of a COM object created by <see cref="NativeDll.CreateComObject"/>.
        /// </summary>
        public class ComObject
        {
            /// <summary>
            /// The instance of a COM object.
            /// </summary>
            public readonly object Instance;

            /// <summary>
            /// CLSID of the object.
            /// </summary>
            public readonly Guid CLSID;

            public ComObject(object instance, Guid guid)
            {
                if (instance == null)
                    throw new ArgumentNullException("instance");

                Instance = instance;
                CLSID = guid;
            }

            /// <summary>
            /// Casts the object to the specified interface type.
            /// </summary>
            public TInterface As<TInterface>() where TInterface : class
            {
                if (Instance is TInterface)
                    return (TInterface)Instance;

                throw new InvalidOperationException(string.Format("The object {0} does not implement the {1} interface.", CLSID, typeof(TInterface).Name));
            }
        }

    }
}