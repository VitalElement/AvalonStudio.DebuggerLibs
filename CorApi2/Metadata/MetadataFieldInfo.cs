//---------------------------------------------------------------------
//  This file is part of the CLR Managed Debugger (mdbg) Sample.
// 
//  Copyright (C) Microsoft Corporation.  All rights reserved.
//---------------------------------------------------------------------
using System;
using System.Reflection;
using System.Collections;
using System.Text;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Diagnostics;

using Microsoft.Samples.Debugging.CorDebug; 
using Microsoft.Samples.Debugging.CorMetadata.NativeApi; 
using Microsoft.Samples.Debugging.CorDebug.NativeApi;
using Microsoft.Samples.Debugging.Extensions;

namespace Microsoft.Samples.Debugging.CorMetadata
{
    public sealed class MetadataFieldInfo : FieldInfo
    {
        internal MetadataFieldInfo(CorApi.Portable.IMetaDataImport importer,uint fieldToken, MetadataType declaringType)
        {
            unsafe {
                m_importer = importer;
                m_fieldToken = fieldToken;
                m_declaringType = declaringType;

                // Initialize
                uint mdTypeDef;
                uint pchField, pcbSigBlob, pdwCPlusTypeFlab, pcchValue, pdwAttr;
                IntPtr ppvSigBlob;
                IntPtr ppvRawValue;
                m_importer.GetFieldProps(m_fieldToken,
                                         out mdTypeDef,
                                         IntPtr.Zero,
                                         0,
                                         out pchField,
                                         out pdwAttr,
                                         out ppvSigBlob,
                                         out pcbSigBlob,
                                         out pdwCPlusTypeFlab,
                                         out ppvRawValue,
                                         out pcchValue
                                         );

                var szField = stackalloc char[(int)pchField];
                
                m_importer.GetFieldProps(m_fieldToken,
                                         out mdTypeDef,
                                         (IntPtr)szField,
                                         pchField,
                                         out pchField,
                                         out pdwAttr,
                                         out ppvSigBlob,
                                         out pcbSigBlob,
                                         out pdwCPlusTypeFlab,
                                         out ppvRawValue,
                                         out pcchValue
                                         );
                m_fieldAttributes = (FieldAttributes)pdwAttr;
                m_name = new string(szField, 0, (int)pchField - 1);

                // Get the values for static literal fields with primitive types
                FieldAttributes staticLiteralField = FieldAttributes.Static | FieldAttributes.HasDefault | FieldAttributes.Literal;
                if ((m_fieldAttributes & staticLiteralField) == staticLiteralField)
                {
                    m_value = ParseDefaultValue(declaringType, ppvSigBlob, ppvRawValue, pcchValue);
                }
                // [Xamarin] Expression evaluator.
                MetadataHelperFunctionsExtensions.GetCustomAttribute(m_importer, m_fieldToken, typeof(DebuggerBrowsableAttribute));
            }
        }

        private static object ParseDefaultValue(MetadataType declaringType, IntPtr ppvSigBlob, IntPtr ppvRawValue, uint rawValueSize)
        {
                IntPtr ppvSigTemp = ppvSigBlob;
                CorCallingConvention callingConv = MetadataHelperFunctions.CorSigUncompressCallingConv(ref ppvSigTemp);
                Debug.Assert(callingConv == CorCallingConvention.Field);

                CorApi.Portable.CorElementType elementType = MetadataHelperFunctions.CorSigUncompressElementType(ref ppvSigTemp);
                if (elementType == CorApi.Portable.CorElementType.ElementTypeValuetype)
                {
                        uint token = MetadataHelperFunctions.CorSigUncompressToken(ref ppvSigTemp);

                        if (token == declaringType.MetadataToken)
                        {
                            // Static literal field of the same type as the enclosing type
                            // may be one of the value fields of an enum
                            if (declaringType.ReallyIsEnum)
                            {
                                // If so, the value will be of the enum's underlying type,
                                // so we change it from VALUETYPE to be that type so that
                                // the following code will get the value
                                elementType = declaringType.EnumUnderlyingType;
                            }                           
                        }
                }

                switch (elementType)
                {
                    case CorApi.Portable.CorElementType.ElementTypeChar:
                        return (char)Marshal.ReadByte(ppvRawValue);
                    case CorApi.Portable.CorElementType.ElementTypeI1:
                        return (sbyte)Marshal.ReadByte(ppvRawValue);
                    case CorApi.Portable.CorElementType.ElementTypeU1:
                        return Marshal.ReadByte(ppvRawValue);
                    case CorApi.Portable.CorElementType.ElementTypeI2:
                        return Marshal.ReadInt16(ppvRawValue);
                    case CorApi.Portable.CorElementType.ElementTypeU2:
                        return (ushort)Marshal.ReadInt16(ppvRawValue);
                    case CorApi.Portable.CorElementType.ElementTypeI4:
                        return Marshal.ReadInt32(ppvRawValue);
                    case CorApi.Portable.CorElementType.ElementTypeU4:
                        return (uint)Marshal.ReadInt32(ppvRawValue);
                    case CorApi.Portable.CorElementType.ElementTypeI8:
                        return Marshal.ReadInt64(ppvRawValue);
                    case CorApi.Portable.CorElementType.ElementTypeU8:
                        return (ulong)Marshal.ReadInt64(ppvRawValue);
                    case CorApi.Portable.CorElementType.ElementTypeI:
                        return Marshal.ReadIntPtr(ppvRawValue);
                    case CorApi.Portable.CorElementType.ElementTypeString:
                        return Marshal.PtrToStringAuto (ppvRawValue, (int)rawValueSize);
                    case CorApi.Portable.CorElementType.ElementTypeR4:
                        unsafe {
                            return *(float*) ppvRawValue.ToPointer ();
                        }
                    case CorApi.Portable.CorElementType.ElementTypeR8:
                        unsafe {
                            return *(double*) ppvRawValue.ToPointer ();
                        }
                    case CorApi.Portable.CorElementType.ElementTypeBoolean:
                        unsafe {
                            return *(bool*) ppvRawValue.ToPointer ();
                        }

                    default:
                        return null;
                }
        }

        public override Object GetValue(Object obj)
        {
            FieldAttributes staticLiteralField = FieldAttributes.Static | FieldAttributes.HasDefault | FieldAttributes.Literal;
            if ((m_fieldAttributes & staticLiteralField) != staticLiteralField)
            {
                throw new InvalidOperationException("Field is not a static literal field.");
            }
            if (m_value == null)
            {
                throw new NotImplementedException("GetValue not implemented for the given field type.");
            }
            else
            {
                return m_value;
            }
        }

        public override void SetValue(Object obj, Object value,BindingFlags invokeAttr,Binder binder,CultureInfo culture)
        {
            throw new NotImplementedException();
        }

		// [Xamarin] Expression evaluator.
		public override object[] GetCustomAttributes (bool inherit)
		{
			if (m_customAttributes == null)
				m_customAttributes = MetadataHelperFunctionsExtensions.GetDebugAttributes (m_importer, m_fieldToken);
			return m_customAttributes;
		}

		// [Xamarin] Expression evaluator.
		public override object[] GetCustomAttributes (Type attributeType, bool inherit)
		{
			ArrayList list = new ArrayList ();
			foreach (object ob in GetCustomAttributes (inherit)) {
				if (attributeType.IsInstanceOfType (ob))
					list.Add (ob);
			}
			return list.ToArray ();
		}

		// [Xamarin] Expression evaluator.
		public override bool IsDefined (Type attributeType, bool inherit)
		{
			return GetCustomAttributes (attributeType, inherit).Length > 0;
		}


        public  override Type FieldType 
        {
            get 
            {
                throw new NotImplementedException();
            }
        }   

        public override RuntimeFieldHandle FieldHandle 
        {
            get 
            {
                throw new NotImplementedException();
            }
        }

        public override FieldAttributes Attributes 
        {
            get 
            {
                return m_fieldAttributes;
            }
        }

        public override MemberTypes MemberType 
        {
            get 
            {
                throw new NotImplementedException();
            }
        }
    
        public override String Name 
        {
            get 
            {
                return m_name;
            }
        }
    
        public override Type DeclaringType 
        {
            get 
            {
                throw new NotImplementedException();
            }
        }
    
        public override Type ReflectedType 
        {
            get 
            {
                throw new NotImplementedException();
            }
        }

        public override int MetadataToken 
        {
            get 
            {
                return (int)m_fieldToken;
            }
        }

        private CorApi.Portable.IMetaDataImport m_importer;
        private uint m_fieldToken;
        private MetadataType m_declaringType;

        private string m_name;
        private FieldAttributes m_fieldAttributes;
        private Object m_value;
		// [Xamarin] Expression evaluator.
		private object[] m_customAttributes;
    }
}
