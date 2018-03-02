//
// MetadataExtensions.cs
//
// Author:
//       Lluis Sanchez <lluis@xamarin.com>
//       Therzok <teromario@yahoo.com>
//
// Copyright (c) 2013 Xamarin Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CorApi2.Metadata.Microsoft.Samples.Debugging.CorMetadata;
using Microsoft.Samples.Debugging.CorDebug.NativeApi;
using Microsoft.Samples.Debugging.CorMetadata;
using Microsoft.Samples.Debugging.CorMetadata.NativeApi;

namespace Microsoft.Samples.Debugging.Extensions
{
	// [Xamarin] Expression evaluator.
	public static class MetadataExtensions
	{
		internal static bool TypeFlagsMatch (bool isPublic, bool isStatic, BindingFlags flags)
		{
			if (isPublic && (flags & BindingFlags.Public) == 0)
				return false;
			if (!isPublic && (flags & BindingFlags.NonPublic) == 0)
				return false;
			if (isStatic && (flags & BindingFlags.Static) == 0)
				return false;
			if (!isStatic && (flags & BindingFlags.Instance) == 0)
				return false;
			return true;
		}

		internal static Type MakeDelegate (Type retType, List<Type> argTypes)
		{
			throw new NotImplementedException ();
		}

		public static Type MakeArray (Type t, List<int> sizes, List<int> loBounds)
		{
			var mt = t as MetadataType;
			if (mt != null) {
				if (sizes == null) {
					sizes = new List<int> ();
					sizes.Add (1);
				}
				mt.m_arraySizes = sizes;
				mt.m_arrayLoBounds = loBounds;
				return mt;
			}
			if (sizes == null || sizes.Count == 1)
				return t.MakeArrayType ();
			return t.MakeArrayType (sizes.Capacity);
		}

		static Type MakeByRefTypeIfNeeded (Type t)
		{
			if (t.IsByRef)
				return t;
			var makeByRefType = t.MakeByRefType ();
			return makeByRefType;
		}

		public static Type MakeByRef (Type t)
		{
			var mt = t as MetadataType;
			if (mt != null) {
				mt.m_isByRef = true;
				return mt;
			}

			return MakeByRefTypeIfNeeded (t);
		}

		public static Type MakePointer (Type t)
		{
			var mt = t as MetadataType;
			if (mt != null) {
				mt.m_isPtr = true;
				return mt;
			}
			return MakeByRefTypeIfNeeded (t);
		}

		public static Type MakeGeneric (Type t, List<Type> typeArgs)
		{
			var mt = (MetadataType)t;
			mt.m_typeArgs = typeArgs;
			return mt;
		}
	}

	// [Xamarin] Expression evaluator.
	[CLSCompliant (false)]
	public static class MetadataHelperFunctionsExtensions
	{
		public static readonly Dictionary<CorApi.Portable.CorElementType, Type> CoreTypes = new Dictionary<CorApi.Portable.CorElementType, Type> ();
		static MetadataHelperFunctionsExtensions ()
		{
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeBoolean, typeof (bool));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeChar, typeof (char));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeI1, typeof (sbyte));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeU1, typeof (byte));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeI2, typeof (short));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeU2, typeof (ushort));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeI4, typeof (int));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeU4, typeof (uint));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeI8, typeof (long));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeU8, typeof (ulong));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeR4, typeof (float));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeR8, typeof (double));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeString, typeof (string));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeI, typeof (IntPtr));
			CoreTypes.Add (CorApi.Portable.CorElementType.ElementTypeU, typeof (UIntPtr));
		}

		internal static void ReadMethodSignature (CorApi.Portable.IMetaDataImport importer, Instantiation instantiation, ref IntPtr pData, out CorCallingConvention cconv, out Type retType, out List<Type> argTypes, out int sentinelIndex)
		{
			cconv = MetadataHelperFunctions.CorSigUncompressCallingConv (ref pData);
			uint numArgs = 0;
			// FIXME: Use number of <T>s.
			uint types = 0;
			sentinelIndex = -1;
			if ((cconv & CorCallingConvention.Generic) == CorCallingConvention.Generic)
				types = MetadataHelperFunctions.CorSigUncompressData (ref pData);

			if (cconv != CorCallingConvention.Field)
				numArgs = MetadataHelperFunctions.CorSigUncompressData (ref pData);

			retType = ReadType (importer, instantiation, ref pData);
			argTypes = new List<Type> ();
			for (int n = 0; n < numArgs; n++) {
				CorElementType elemType;
				unsafe {
					var pByte = (byte*) pData;
					var b = *pByte;
					elemType = (CorElementType) b;

					if (elemType == CorElementType.ELEMENT_TYPE_SENTINEL) {
						// the case when SENTINEL is presented in a separate position, so we have to increment the pointer
						sentinelIndex = n;
						pData = (IntPtr) (pByte + 1);
					}
					else if ((elemType & CorElementType.ELEMENT_TYPE_SENTINEL) == CorElementType.ELEMENT_TYPE_SENTINEL) {
						// SENTINEL is just a flag on element type, so we haven't to promote the pointer
						sentinelIndex = n;
					}
				}
				argTypes.Add (ReadType (importer, instantiation, ref pData));
			}
		}

		static Type ReadType (CorApi.Portable.IMetaDataImport importer, Instantiation instantiation, ref IntPtr pData)
		{
			CorApi.Portable.CorElementType et;
			unsafe {
				var pBytes = (byte*)pData;
				et = (CorApi.Portable.CorElementType) (*pBytes);
				pData = (IntPtr) (pBytes + 1);
			}

			if ((et & CorApi.Portable.CorElementType.ElementTypeSentinel) == CorApi.Portable.CorElementType.ElementTypeSentinel) {
				et ^= CorApi.Portable.CorElementType.ElementTypeSentinel; // substract SENTINEL bits from element type to get clean ET
			}

			switch (et)
			{
			case CorApi.Portable.CorElementType.ElementTypeVoid: return typeof (void);
			case CorApi.Portable.CorElementType.ElementTypeBoolean: return typeof (bool);
			case CorApi.Portable.CorElementType.ElementTypeChar: return typeof (char);
			case CorApi.Portable.CorElementType.ElementTypeI1: return typeof (sbyte);
			case CorApi.Portable.CorElementType.ElementTypeU1: return typeof (byte);
			case CorApi.Portable.CorElementType.ElementTypeI2: return typeof (short);
			case CorApi.Portable.CorElementType.ElementTypeU2: return typeof (ushort);
			case CorApi.Portable.CorElementType.ElementTypeI4: return typeof (int);
			case CorApi.Portable.CorElementType.ElementTypeU4: return typeof (uint);
			case CorApi.Portable.CorElementType.ElementTypeI8: return typeof (long);
			case CorApi.Portable.CorElementType.ElementTypeU8: return typeof (ulong);
			case CorApi.Portable.CorElementType.ElementTypeR4: return typeof (float);
			case CorApi.Portable.CorElementType.ElementTypeR8: return typeof (double);
			case CorApi.Portable.CorElementType.ElementTypeString: return typeof (string);
			case CorApi.Portable.CorElementType.ElementTypeI: return typeof (IntPtr);
			case CorApi.Portable.CorElementType.ElementTypeU: return typeof (UIntPtr);
			case CorApi.Portable.CorElementType.ElementTypeObject: return typeof (object);
			case CorApi.Portable.CorElementType.ElementTypeTypedbyref: return typeof(TypedReference);

			case CorApi.Portable.CorElementType.ElementTypeVar: {
					var index = MetadataHelperFunctions.CorSigUncompressData (ref pData);
					if (index < instantiation.TypeArgs.Count) {
						return instantiation.TypeArgs[(int) index];
					}
					return new TypeGenericParameter((int) index);
				}
			case CorApi.Portable.CorElementType.ElementTypeMvar: {
					// Generic args in methods not supported. Return a dummy type.
					var index = MetadataHelperFunctions.CorSigUncompressData (ref pData);
					return new MethodGenericParameter((int) index);
				}

			case CorApi.Portable.CorElementType.ElementTypeGenericinst: {
					Type t = ReadType (importer, instantiation, ref pData);
					var typeArgs = new List<Type> ();
					uint num = MetadataHelperFunctions.CorSigUncompressData (ref pData);
					for (int n=0; n<num; n++) {
						typeArgs.Add (ReadType (importer, instantiation, ref pData));
					}
					return MetadataExtensions.MakeGeneric (t, typeArgs);
				}

			case CorApi.Portable.CorElementType.ElementTypePtr: {
					Type t = ReadType (importer, instantiation, ref pData);
					return MetadataExtensions.MakePointer (t);
				}

			case CorApi.Portable.CorElementType.ElementTypeByref: {
					Type t = ReadType (importer, instantiation, ref pData);
					return MetadataExtensions.MakeByRef(t);
				}

			case CorApi.Portable.CorElementType.ElementTypeEnd:
			case CorApi.Portable.CorElementType.ElementTypeValuetype:
			case CorApi.Portable.CorElementType.ElementTypeClass: {
					uint token = MetadataHelperFunctions.CorSigUncompressToken (ref pData);
					return new MetadataType (importer, token);
				}

			case CorApi.Portable.CorElementType.ElementTypeArray: {
					Type t = ReadType (importer, instantiation, ref pData);
					int rank = (int)MetadataHelperFunctions.CorSigUncompressData (ref pData);
					if (rank == 0)
						return MetadataExtensions.MakeArray (t, null, null);

					uint numSizes = MetadataHelperFunctions.CorSigUncompressData (ref pData);
					var sizes = new List<int> (rank);
					for (int n = 0; n < numSizes && n < rank; n++)
						sizes.Add ((int)MetadataHelperFunctions.CorSigUncompressData (ref pData));

					uint numLoBounds = MetadataHelperFunctions.CorSigUncompressData (ref pData);
					var loBounds = new List<int> (rank);
					for (int n = 0; n < numLoBounds && n < rank; n++)
						loBounds.Add ((int)MetadataHelperFunctions.CorSigUncompressData (ref pData));

					return MetadataExtensions.MakeArray (t, sizes, loBounds);
				}

			case CorApi.Portable.CorElementType.ElementTypeSzarray: {
					Type t = ReadType (importer, instantiation, ref pData);
					return MetadataExtensions.MakeArray (t, null, null);
				}

			case CorApi.Portable.CorElementType.ElementTypeFnptr: {
					CorCallingConvention cconv;
					Type retType;
					List<Type> argTypes;
					int sentinelIndex;
					ReadMethodSignature (importer, instantiation, ref pData, out cconv, out retType, out argTypes, out sentinelIndex);
					return MetadataExtensions.MakeDelegate (retType, argTypes);
				}

			case CorApi.Portable.CorElementType.ElementTypeCmodReqd:
			case CorApi.Portable.CorElementType.ElementTypeCmodOpt: {
					uint token = MetadataHelperFunctions.CorSigUncompressToken (ref pData);
					return new MetadataType (importer, token);
				}

			case CorApi.Portable.CorElementType.ElementTypeInternal:
				return typeof(object); // hack to avoid the exceptions. CLR spec says that this type should never occurs, but it occurs sometimes, mystics

			case (CorApi.Portable.CorElementType)CorApi.Portable.CorElementTypeExtra.ELEMENT_TYPE_NATIVE_ARRAY_TEMPLATE_ZAPSIG:
			case (CorApi.Portable.CorElementType)CorApi.Portable.CorElementTypeExtra.ELEMENT_TYPE_NATIVE_VALUETYPE_ZAPSIG:
				return ReadType (importer, instantiation, ref pData);

			case (CorApi.Portable.CorElementType)CorApi.Portable.CorElementTypeExtra.ELEMENT_TYPE_CANON_ZAPSIG:
				return typeof(object); // this is representation of __Canon type, but it's inaccessible, using object instead
			}
			throw new NotSupportedException ("Unknown sig element type: " + et);
		}

		static readonly object[] emptyAttributes = new object[0];

		static internal object[] GetDebugAttributes (CorApi.Portable.IMetaDataImport importer, uint token)
		{
			var attributes = new ArrayList ();
			object attr = GetCustomAttribute (importer, token, typeof (System.Diagnostics.DebuggerTypeProxyAttribute));
			if (attr != null)
				attributes.Add (attr);
			attr = GetCustomAttribute (importer, token, typeof (System.Diagnostics.DebuggerDisplayAttribute));
			if (attr != null)
				attributes.Add (attr);
			attr = GetCustomAttribute (importer, token, typeof (System.Diagnostics.DebuggerBrowsableAttribute));
			if (attr != null)
				attributes.Add (attr);
			attr = GetCustomAttribute (importer, token, typeof (System.Runtime.CompilerServices.CompilerGeneratedAttribute));
			if (attr != null)
				attributes.Add (attr);
			attr = GetCustomAttribute (importer, token, typeof (System.Diagnostics.DebuggerHiddenAttribute));
			if (attr != null)
				attributes.Add (attr);
			attr = GetCustomAttribute (importer, token, typeof (System.Diagnostics.DebuggerStepThroughAttribute));
			if (attr != null)
				attributes.Add (attr);
			attr = GetCustomAttribute (importer, token, typeof (System.Diagnostics.DebuggerNonUserCodeAttribute));
			if (attr != null)
				attributes.Add (attr);
			attr = GetCustomAttribute (importer, token, typeof (System.Diagnostics.DebuggerStepperBoundaryAttribute));
			if (attr != null)
				attributes.Add (attr);

			return attributes.Count == 0 ? emptyAttributes : attributes.ToArray ();
		}

		// [Xamarin] Expression evaluator.
		static internal object GetCustomAttribute (CorApi.Portable.IMetaDataImport importer, uint token, Type type)
		{
			uint sigSize;
			IntPtr ppvSig;
            try
            {
                importer.GetCustomAttributeByName(token, type.FullName, out ppvSig, out sigSize);
            }
            catch(SharpGen.Runtime.SharpGenException)
            {
                return null;
            }

            if(sigSize == 0)
            {
                return null;
            }
			
			var data = new byte[sigSize];
			Marshal.Copy (ppvSig, data, 0, (int)sigSize);
			var br = new BinaryReader (new MemoryStream (data));

			// Prolog
			if (br.ReadUInt16 () != 1)
				throw new InvalidOperationException ("Incorrect attribute prolog");

			ConstructorInfo ctor = type.GetConstructors ()[0];
			ParameterInfo[] pars = ctor.GetParameters ();

			var args = new object[pars.Length];

			// Fixed args
			for (int n=0; n<pars.Length; n++)
				args [n] = ReadValue (br, pars[n].ParameterType);

			object ob = Activator.CreateInstance (type, args);

			// Named args
			uint nargs = br.ReadUInt16 ();
			for (; nargs > 0; nargs--) {
				byte fieldOrProp = br.ReadByte ();
				byte atype = br.ReadByte ();

				// Boxed primitive
				if (atype == 0x51)
					atype = br.ReadByte ();
				var et = (CorApi.Portable.CorElementType) atype;
				string pname = br.ReadString ();
				object val = ReadValue (br, CoreTypes [et]);

				if (fieldOrProp == 0x53) {
					FieldInfo fi = type.GetField (pname);
					fi.SetValue (ob, val);
				}
				else {
					PropertyInfo pi = type.GetProperty (pname);
					pi.SetValue (ob, val, null);
				}
			}
			return ob;
		}

		// [Xamarin] Expression evaluator.
		static object ReadValue (BinaryReader br, Type type)
		{
			if (type.IsEnum) {
				object ob = ReadValue (br, Enum.GetUnderlyingType (type));
				return Enum.ToObject (type, Convert.ToInt64 (ob));
			}
			if (type == typeof (string) || type == typeof(Type))
				return br.ReadString ();
			if (type == typeof (int))
				return br.ReadInt32 ();
			throw new InvalidOperationException ("Can't parse value of type: " + type);
		}
	}
}

