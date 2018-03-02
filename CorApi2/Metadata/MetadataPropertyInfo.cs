using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;

using Microsoft.Samples.Debugging.CorDebug;
using Microsoft.Samples.Debugging.CorMetadata.NativeApi;
using Microsoft.Samples.Debugging.CorDebug.NativeApi;
using Microsoft.Samples.Debugging.Extensions;

namespace Microsoft.Samples.Debugging.CorMetadata
{
	public class MetadataPropertyInfo: PropertyInfo
	{
		private CorApi.Portable.IMetaDataImport m_importer;
		private uint m_propertyToken;
		private MetadataType m_declaringType;
		private object[] m_customAttributes;

		private string m_name;
		private PropertyAttributes m_propAttributes;

		uint m_pmdSetter;
		uint m_pmdGetter;

		MetadataMethodInfo m_setter;
		MetadataMethodInfo m_getter;

		internal MetadataPropertyInfo (CorApi.Portable.IMetaDataImport importer, uint propertyToken, MetadataType declaringType)
		{
            unsafe
            {
                m_importer = importer;
                m_propertyToken = propertyToken;
                m_declaringType = declaringType;

                uint mdTypeDef;
                uint pchProperty;
                uint pdwPropFlags;
                IntPtr ppvSig;
                uint pbSig;
                uint pdwCPlusTypeFlag;
                IntPtr ppDefaultValue;
                uint pcchDefaultValue;
                uint[] rmdOtherMethod = new uint[0];
                uint pcOtherMethod;

                m_importer.GetPropertyProps(
                    m_propertyToken,
                    out mdTypeDef,
                    IntPtr.Zero,
                    0,
                    out pchProperty,
                    out pdwPropFlags,
                    out ppvSig,
                    out pbSig,
                    out pdwCPlusTypeFlag,
                    out ppDefaultValue,
                    out pcchDefaultValue,
                    out m_pmdSetter,
                    out m_pmdGetter,
                    rmdOtherMethod,
                    0,
                    out pcOtherMethod);

                var szProperty = stackalloc char[(int)pchProperty];
                m_importer.GetPropertyProps(
                    m_propertyToken,
                    out mdTypeDef,
                    (IntPtr)szProperty,
                    pchProperty,
                    out pchProperty,
                    out pdwPropFlags,
                    out ppvSig,
                    out pbSig,
                    out pdwCPlusTypeFlag,
                    out ppDefaultValue,
                    out pcchDefaultValue,
                    out m_pmdSetter,
                    out m_pmdGetter,
                    rmdOtherMethod,
                    0,
                    out pcOtherMethod);

                m_propAttributes = (PropertyAttributes)pdwPropFlags;
                m_name = new string(szProperty, 0, (int)pchProperty - 1);
            }

			MetadataHelperFunctionsExtensions.GetCustomAttribute (importer, propertyToken, typeof (System.Diagnostics.DebuggerBrowsableAttribute));

			if (!m_importer.IsValidToken (m_pmdGetter))
				m_pmdGetter = 0;

			if (!m_importer.IsValidToken (m_pmdSetter))
				m_pmdSetter = 0;
		}

		public override PropertyAttributes Attributes
		{
			get { return m_propAttributes; }
		}

		public override bool CanRead
		{
			get { return m_pmdGetter != 0; }
		}

		public override bool CanWrite
		{
			get { return m_pmdSetter != 0; }
		}

		public override MethodInfo[] GetAccessors (bool nonPublic)
		{
			throw new NotImplementedException ();
		}

		public override MethodInfo GetGetMethod (bool nonPublic)
		{
			if (m_pmdGetter == 0)
				return null;

			if (m_getter == null)
				m_getter = new MetadataMethodInfo (m_importer, m_pmdGetter, Instantiation.Create (m_declaringType.GenericTypeArguments));

			if (nonPublic || m_getter.IsPublic)
				return m_getter;
			return null;
		}

		public override ParameterInfo[] GetIndexParameters ( )
		{
			MethodInfo mi = GetGetMethod ();
			if (mi == null)
				return new ParameterInfo[0];
			return mi.GetParameters ();
		}

		public override MethodInfo GetSetMethod (bool nonPublic)
		{
			if (m_pmdSetter == 0)
				return null;

			if (m_setter == null)
				m_setter = new MetadataMethodInfo (m_importer, m_pmdSetter, Instantiation.Create (m_declaringType.GenericTypeArguments));

			if (nonPublic || m_setter.IsPublic)
				return m_setter;
			return null;
		}

		public override object GetValue (object obj, BindingFlags invokeAttr, Binder binder, object[] index, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException ();
		}

		public override Type PropertyType
		{
			get { throw new NotImplementedException (); }
		}

		public override void SetValue (object obj, object value, BindingFlags invokeAttr, Binder binder, object[] index, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException ();
		}

		public override Type DeclaringType
		{
			get {
				return m_declaringType;
			}
		}

		public override bool IsDefined (Type attributeType, bool inherit)
		{
			return GetCustomAttributes (attributeType, inherit).Length > 0;
		}

		public override object[] GetCustomAttributes (Type attributeType, bool inherit)
		{
			ArrayList list = new ArrayList ();
			foreach (object ob in GetCustomAttributes (inherit)) {
				if (attributeType.IsInstanceOfType (ob))
					list.Add (ob);
			}
			return list.ToArray ();
		}

		public override object[] GetCustomAttributes (bool inherit)
		{
			if (m_customAttributes == null)
				m_customAttributes = MetadataHelperFunctionsExtensions.GetDebugAttributes (m_importer, m_propertyToken);
			return m_customAttributes;
		}

		public override string Name
		{
			get { return m_name; }
		}

		public override Type ReflectedType
		{
			get { throw new NotImplementedException (); }
		}
	}
}
