// Util.cs
//
// Author:
//   Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
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
//
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.Reflection;
using System.Text;
using CorApi2.Metadata.Microsoft.Samples.Debugging.CorMetadata;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Samples.Debugging.Extensions;
using Microsoft.Samples.Debugging.CorMetadata;

namespace Mono.Debugging.Win32
{
	public class CorObjectAdaptor: ObjectValueAdaptor
	{
		public override bool IsPrimitive (EvaluationContext ctx, object val)
		{
			object v = GetRealObject (ctx, val);
			return (v is CorApi.Portable.GenericValue) || (v is CorApi.Portable.StringValue);
		}

		public override bool IsPointer (EvaluationContext ctx, object val)
		{
			var type = (CorApi.Portable.Type) GetValueType (ctx, val);
			return IsPointer (type);
		}

		public override bool IsEnum (EvaluationContext ctx, object val)
		{
			if (!(val is CorValRef))
				return false;
			var type = (CorApi.Portable.Type) GetValueType (ctx, val);
			return IsEnum (ctx, type);
		}

		public override bool IsArray (EvaluationContext ctx, object val)
		{
			return GetRealObject (ctx, val) is CorApi.Portable.ArrayValue;
		}
		
		public override bool IsString (EvaluationContext ctx, object val)
		{
			return GetRealObject (ctx, val) is CorApi.Portable.StringValue;
		}

		public override bool IsNull (EvaluationContext ctx, object gval)
		{
			if (gval == null)
				return true;
			var val = gval as CorValRef;
			if (val == null)
				return true;
			if (val.Val == null || ((val.Val is CorApi.Portable.ReferenceValue) && ((CorApi.Portable.ReferenceValue) val.Val).IsNull))
				return true;

			var obj = GetRealObject (ctx, val);
			return (obj is CorApi.Portable.ReferenceValue) && ((CorApi.Portable.ReferenceValue)obj).IsNull;
		}

		public override bool IsValueType (object type)
		{
			return ((CorApi.Portable.Type)type).CorType == CorApi.Portable.CorElementType.ElementTypeValuetype;
		}

		public override bool IsClass (EvaluationContext ctx, object type)
		{
			var t = (CorApi.Portable.Type) type;
			var cctx = (CorEvaluationContext)ctx;
			Type tt;
			if (t.CorType == CorApi.Portable.CorElementType.ElementTypeString ||
			   t.CorType == CorApi.Portable.CorElementType.ElementTypeArray ||
			   t.CorType == CorApi.Portable.CorElementType.ElementTypeSzarray)
				return true;
			// Primitive check
			if (MetadataHelperFunctionsExtensions.CoreTypes.TryGetValue (t.CorType, out tt))
				return false;

			if (IsIEnumerable (t, cctx.Session))
				return false;

			return (t.CorType == CorApi.Portable.CorElementType.ElementTypeClass && t.Class != null) || IsValueType (t);
		}

		public override bool IsGenericType (EvaluationContext ctx, object type)
		{
			return (((CorApi.Portable.Type)type).CorType == CorApi.Portable.CorElementType.ElementTypeGenericinst) || base.IsGenericType (ctx, type);
		}

		public override bool NullableHasValue (EvaluationContext ctx, object type, object obj)
		{
			ValueReference hasValue = GetMember (ctx, type, obj, "hasValue");

			return (bool) hasValue.ObjectValue;
		}

		public override ValueReference NullableGetValue (EvaluationContext ctx, object type, object obj)
		{
			return GetMember (ctx, type, obj, "value");
		}

		public override string GetTypeName (EvaluationContext ctx, object gtype)
		{
			CorApi.Portable.Type type = (CorApi.Portable.Type) gtype;
			
			CorEvaluationContext cctx = (CorEvaluationContext) ctx;
			Type t;
			if (MetadataHelperFunctionsExtensions.CoreTypes.TryGetValue (type.CorType, out t))
				return t.FullName;
			try {
				if (type.CorType == CorApi.Portable.CorElementType.ElementTypeArray || type.CorType == CorApi.Portable.CorElementType.ElementTypeSzarray)
					return GetTypeName (ctx, type.FirstTypeParameter) + "[" + new string (',', (int)type.Rank - 1) + "]";

				if (type.CorType == CorApi.Portable.CorElementType.ElementTypeByref)
					return GetTypeName (ctx, type.FirstTypeParameter) + "&";

				if (type.CorType == CorApi.Portable.CorElementType.ElementTypePtr)
					return GetTypeName (ctx, type.FirstTypeParameter) + "*";
				
				return type.GetTypeInfo (cctx.Session).FullName;
			}
			catch (Exception ex) {
				DebuggerLoggingService.LogError ("Exception in GetTypeName()", ex);
				return "[Unknown type]";
			}
		}

		public override object GetValueType (EvaluationContext ctx, object val)
		{
			if (val == null)
				return GetType (ctx, "System.Object");

			var realObject = GetRealObject (ctx, val);
			if (realObject == null)
				return GetType (ctx, "System.Object");;
			return realObject.ExactType;
		}
		
		public override object GetBaseType (EvaluationContext ctx, object type)
		{
			return ((CorApi.Portable.Type) type).Base;
		}

		protected override object GetBaseTypeWithAttribute (EvaluationContext ctx, object type, object attrType)
		{
			var wctx = (CorEvaluationContext) ctx;
			var attr = ((CorApi.Portable.Type) attrType).GetTypeInfo (wctx.Session);
			var tm = type as CorApi.Portable.Type;

			while (tm != null) {
				var t = tm.GetTypeInfo (wctx.Session);

				if (t.GetCustomAttributes (attr, false).Any ())
					return tm;

				tm = tm.Base;
			}

			return null;
		}

		public override object[] GetTypeArgs (EvaluationContext ctx, object type)
		{
			CorApi.Portable.Type[] types = ((CorApi.Portable.Type)type).TypeParameters;
			return CastArray<object> (types);
		}

		static IEnumerable<Type> GetAllTypes (EvaluationContext gctx)
		{
			CorEvaluationContext ctx = (CorEvaluationContext) gctx;
			foreach (CorApi.Portable.Module mod in ctx.Session.GetAllModules()) {
				CorMetadataImport mi = ctx.Session.GetMetadataForModule (mod);
				if (mi != null) {
					foreach (Type t in mi.DefinedTypes)
						yield return t;
				}
			}
		}

		readonly Dictionary<string, CorApi.Portable.Type> nameToTypeCache = new Dictionary<string, CorApi.Portable.Type> ();
		readonly Dictionary<CorApi.Portable.Type, string> typeToNameCache = new Dictionary<CorApi.Portable.Type, string> ();
		readonly HashSet<string> unresolvedNames = new HashSet<string> ();


		string GetCacheName (string name, CorApi.Portable.Type[] typeArgs)
		{
			if (typeArgs == null || typeArgs.Length == 0)
				return name;
			var result = new StringBuilder(name + "<");
			for (int i = 0; i < typeArgs.Length; i++) {
				string currentTypeName;
				if (!typeToNameCache.TryGetValue (typeArgs[i], out currentTypeName)) {
					DebuggerLoggingService.LogMessage ("Can't get cached name for generic type {0} because it's substitution type isn't found in cache", name);
					return null; //Unable to resolve? Don't cache. This should never happen.
				}
				result.Append (currentTypeName);
				if (i < typeArgs.Length - 1)
					result.Append (",");
			}
			result.Append (">");
			return result.ToString();
		}

		public override object GetType (EvaluationContext gctx, string name, object[] gtypeArgs)
		{
			if (string.IsNullOrEmpty (name))
				return null;
			CorApi.Portable.Type[] typeArgs = CastArray<CorApi.Portable.Type> (gtypeArgs);
			CorEvaluationContext ctx = (CorEvaluationContext)gctx;
			var callingModule = ctx.Frame.Function.Class.Module;
			var callingDomain = callingModule.Assembly.AppDomain;
			string domainPrefixedName = string.Format ("{0}:{1}", callingDomain.Id, name);
			string cacheName = GetCacheName (domainPrefixedName, typeArgs);
			CorApi.Portable.Type typeFromCache;

			if (!string.IsNullOrEmpty (cacheName) && nameToTypeCache.TryGetValue (cacheName, out typeFromCache)) {
				return typeFromCache;
			}
			if (unresolvedNames.Contains (cacheName ?? domainPrefixedName))
				return null;
			foreach (CorApi.Portable.Module mod in ctx.Session.GetModules (callingDomain)) {
				var mi = ctx.Session.GetMetadataForModule (mod);
				if (mi != null) {
					var token = mi.GetTypeTokenFromName (name);
					if (token == CorMetadataImport.TokenNotFound)
						continue;
					var t = mi.GetType(token);
					CorApi.Portable.Class cls = mod.GetClassFromToken ((uint)t.MetadataToken);
					CorApi.Portable.Type foundType = cls.GetParameterizedType (CorApi.Portable.CorElementType.ElementTypeClass, typeArgs);
					if (foundType != null) {
						if (!string.IsNullOrEmpty (cacheName)) {
							nameToTypeCache[cacheName] = foundType;
							typeToNameCache[foundType] = cacheName;
						}
						return foundType;
					}
				}
			}
			unresolvedNames.Add (cacheName ?? domainPrefixedName);
			return null;
		}

		static T[] CastArray<T> (object[] array)
		{
			if (array == null)
				return null;
			T[] ret = new T[array.Length];
			Array.Copy (array, ret, array.Length);
			return ret;
		}

        public override string CallToString(EvaluationContext ctx, object objr)
        {
			CorApi.Portable.Value obj = GetRealObject(ctx, objr);

            if ((obj is CorApi.Portable.ReferenceValue) && ((CorApi.Portable.ReferenceValue)obj).IsNull)
                return string.Empty;

            var stringVal = obj as CorApi.Portable.StringValue;
            if (stringVal != null)
                return stringVal.String;

			var genericVal = obj as CorApi.Portable.GenericValue;
			if (genericVal != null)
			{
				return genericVal.GetValue().ToString();
			}

            var arr = obj as CorApi.Portable.ArrayValue;
            if (arr != null)
            {
                var tn = new StringBuilder (GetDisplayTypeName (ctx, arr.ExactType.FirstTypeParameter));
                tn.Append("[");
                uint[] dims = arr.GetDimensions();
                for (int n = 0; n < dims.Length; n++)
                {
                    if (n > 0)
                        tn.Append(',');
                    tn.Append(dims[n]);
                }
                tn.Append("]");
                return tn.ToString();
            }

            var cctx = (CorEvaluationContext)ctx;
            var co = obj as CorApi.Portable.ObjectValue;
            if (co != null)
            {
                if (IsEnum (ctx, co.ExactType))
                {
                    MetadataType rt = co.ExactType.GetTypeInfo(cctx.Session) as MetadataType;
                    bool isFlags = rt != null && rt.ReallyIsFlagsEnum;
                    string enumName = GetTypeName(ctx, co.ExactType);
                    ValueReference val = GetMember(ctx, null, objr, "value__");
                    ulong nval = (ulong)System.Convert.ChangeType(val.ObjectValue, typeof(ulong));
                    ulong remainingFlags = nval;
                    string flags = null;
                    foreach (ValueReference evals in GetMembers(ctx, co.ExactType, null, BindingFlags.Public | BindingFlags.Static))
                    {
                        ulong nev = (ulong)System.Convert.ChangeType(evals.ObjectValue, typeof(ulong));
                        if (nval == nev)
                            return evals.Name;
                        if (isFlags && nev != 0 && (nval & nev) == nev)
                        {
                            if (flags == null)
                                flags = enumName + "." + evals.Name;
                            else
                                flags += " | " + enumName + "." + evals.Name;
                            remainingFlags &= ~nev;
                        }
                    }
                    if (isFlags)
                    {
                        if (remainingFlags == nval)
                            return nval.ToString ();
                        if (remainingFlags != 0)
                            flags += " | " + remainingFlags;
                        return flags;
                    }
                    else
                        return nval.ToString ();
                }

				var targetType = (CorApi.Portable.Type)GetValueType (ctx, objr);

				var methodInfo = OverloadResolve (cctx, "ToString", targetType, new CorApi.Portable.Type[0], BindingFlags.Public | BindingFlags.Instance, false);
				if (methodInfo != null && methodInfo.Item1.DeclaringType != null && methodInfo.Item1.DeclaringType.FullName != "System.Object") {
		            var args = new object[0];
					object ores = RuntimeInvoke (ctx, targetType, objr, "ToString", new object[0], args, args);
					var res = GetRealObject (ctx, ores) as CorApi.Portable.StringValue;
                    if (res != null)
                        return res.String;
                }

				return GetDisplayTypeName (ctx, targetType);
            }

            return base.CallToString(ctx, obj);
        }

		public override object CreateTypeObject (EvaluationContext ctx, object type)
		{
			var t = (CorApi.Portable.Type)type;
			string tname = GetTypeName (ctx, t) + ", " + System.IO.Path.GetFileNameWithoutExtension (t.Class.Module.Assembly.Name);
			var stype = (CorApi.Portable.Type) GetType (ctx, "System.Type");
			object[] argTypes = { GetType (ctx, "System.String") };
			object[] argVals = { CreateValue (ctx, tname) };
			return RuntimeInvoke (ctx, stype, null, "GetType", argTypes, argVals);
		}

		public CorValRef GetBoxedArg (CorEvaluationContext ctx, CorValRef val, Type argType)
		{
			// Boxes a value when required
			if (argType == typeof (object) && IsValueType (ctx, val))
				return Box (ctx, val);
			else
				return val;
		}

		static bool IsValueType (CorEvaluationContext ctx, CorValRef val)
		{
			var v = GetRealObject (ctx, val);
			if (v.Type == CorApi.Portable.CorElementType.ElementTypeValuetype)
				return true;
			return v is CorApi.Portable.GenericValue;
		}

		CorValRef Box (CorEvaluationContext ctx, CorValRef val)
		{
			CorValRef arr = new CorValRef (delegate {
				return ctx.Session.NewArray (ctx, (CorApi.Portable.Type)GetValueType (ctx, val), 1);
			});
			ArrayAdaptor realArr = new ArrayAdaptor (ctx, new CorValRef<CorApi.Portable.ArrayValue> (() => (CorApi.Portable.ArrayValue) GetRealObject (ctx, arr)));
			realArr.SetElement (new [] { 0 }, val);
			var at = (CorApi.Portable.Type) GetType (ctx, "System.Array");
			object[] argTypes = { GetType (ctx, "System.Int32") };
			return (CorValRef)RuntimeInvoke (ctx, at, arr, "GetValue", argTypes, new object[] { CreateValue (ctx, 0) });
		}

		public override bool HasMethod (EvaluationContext gctx, object gtargetType, string methodName, object[] ggenericArgTypes, object[] gargTypes, BindingFlags flags)
		{
			// FIXME: support generic methods by using the genericArgTypes parameter
			var targetType = (CorApi.Portable.Type) gtargetType;
			var argTypes = gargTypes != null ? CastArray<CorApi.Portable.Type> (gargTypes) : null;
			CorEvaluationContext ctx = (CorEvaluationContext)gctx;
			flags = flags | BindingFlags.Public | BindingFlags.NonPublic;

			return OverloadResolve (ctx, methodName, targetType, argTypes, flags, false) != null;
		}

		public override object RuntimeInvoke (EvaluationContext gctx, object gtargetType, object gtarget, string methodName, object[] ggenericArgTypes, object[] gargTypes, object[] gargValues)
		{
			// FIXME: support generic methods by using the genericArgTypes parameter
			var targetType = (CorApi.Portable.Type) gtargetType;
			var target = (CorValRef) gtarget;
			var argTypes = CastArray< CorApi.Portable.Type> (gargTypes);
			var argValues = CastArray<CorValRef> (gargValues);

			BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
			if (target != null)
				flags |= BindingFlags.Instance;
			else
				flags |= BindingFlags.Static;

			CorEvaluationContext ctx = (CorEvaluationContext)gctx;
			var methodInfo = OverloadResolve (ctx, methodName, targetType, argTypes, flags, true);
			if (methodInfo == null)
				return null;
			var method = methodInfo.Item1;
			var methodOwner = methodInfo.Item2;
			ParameterInfo[] parameters = method.GetParameters ();
			// TODO: Check this.
			for (int n = 0; n < parameters.Length; n++) {
				if (parameters[n].ParameterType == typeof(object) && IsValueType (ctx, argValues[n]) && !IsEnum (ctx, argValues[n]))
					argValues[n] = Box (ctx, argValues[n]);
			}

			CorValRef v = new CorValRef (delegate {
				CorApi.Portable.Module mod = null;
				if (methodOwner.CorType == CorApi.Portable.CorElementType.ElementTypeArray || methodOwner.CorType == CorApi.Portable.CorElementType.ElementTypeSzarray
					|| MetadataHelperFunctionsExtensions.CoreTypes.ContainsKey (methodOwner.CorType)) {
					mod = ((CorApi.Portable.Type) ctx.Adapter.GetType (ctx, "System.Object")).Class.Module;
				}
				else {
					mod = methodOwner.Class.Module;
				}
				var func = mod.GetFunctionFromToken ((uint)method.MetadataToken);
				var args = new CorApi.Portable.Value[argValues.Length];
				for (int n = 0; n < args.Length; n++)
					args[n] = argValues[n].Val;
				if (methodOwner.CorType == CorApi.Portable.CorElementType.ElementTypeArray || methodOwner.CorType == CorApi.Portable.CorElementType.ElementTypeSzarray
					|| MetadataHelperFunctionsExtensions.CoreTypes.ContainsKey (methodOwner.CorType)) {
					return ctx.RuntimeInvoke (func, new CorApi.Portable.Type[0], target != null ? target.Val : null, args);
				}
				else {
					return ctx.RuntimeInvoke (func, methodOwner.TypeParameters, target != null ? target.Val : null, args);
				}
			});
			return v.Val == null ? null : v;
		}

		/// <summary>
		/// Returns a pair of method info and a type on which it was resolved
		/// </summary>
		/// <param name="ctx"></param>
		/// <param name="methodName"></param>
		/// <param name="type"></param>
		/// <param name="argtypes"></param>
		/// <param name="flags"></param>
		/// <param name="throwIfNotFound"></param>
		/// <returns></returns>
		Tuple<MethodInfo, CorApi.Portable.Type> OverloadResolve (CorEvaluationContext ctx, string methodName, CorApi.Portable.Type type, CorApi.Portable.Type[] argtypes, BindingFlags flags, bool throwIfNotFound)
		{
			var candidates = new List<Tuple<MethodInfo, CorApi.Portable.Type>> ();
			var currentType = type;

			while (currentType != null) {
				Type rtype = currentType.GetTypeInfo (ctx.Session);
				foreach (MethodInfo met in rtype.GetMethods (flags)) {
					if (met.Name == methodName || (!ctx.CaseSensitive && met.Name.Equals (methodName, StringComparison.CurrentCultureIgnoreCase))) {
						if (argtypes == null)
							return Tuple.Create (met, currentType);
						ParameterInfo[] pars = met.GetParameters ();
						if (pars.Length == argtypes.Length)
							candidates.Add (Tuple.Create (met, currentType));
					}
				}

				if (argtypes == null && candidates.Count > 0)
					break; // when argtypes is null, we are just looking for *any* match (not a specific match)

				if (methodName == ".ctor")
					break; // Can't create objects using constructor from base classes
				if ((rtype.BaseType == null && rtype.FullName != "System.Object") ||
				    currentType.CorType == CorApi.Portable.CorElementType.ElementTypeArray ||
				    currentType.CorType == CorApi.Portable.CorElementType.ElementTypeSzarray ||
				    currentType.CorType == CorApi.Portable.CorElementType.ElementTypeString) {
					currentType = ctx.Adapter.GetType (ctx, "System.Object") as CorApi.Portable.Type;
				} else if (rtype.BaseType != null && rtype.BaseType.FullName == "System.ValueType") {
					currentType = ctx.Adapter.GetType (ctx, "System.ValueType") as CorApi.Portable.Type;
				} else {
					// if the currentType is not a class .Base throws an exception ArgumentOutOfRange (thx for coreclr repo for figure it out)
					try {
						currentType = currentType.Base;
					}
					catch (Exception) {
						currentType = null;
					}
				}
			}

			return OverloadResolve (ctx, GetTypeName (ctx, type), methodName, argtypes, candidates, throwIfNotFound);
		}

		bool IsApplicable (CorEvaluationContext ctx, MethodInfo method, CorApi.Portable.Type[] types, out string error, out int matchCount)
		{
			ParameterInfo[] mparams = method.GetParameters ();
			matchCount = 0;

			for (int i = 0; i < types.Length; i++) {

				Type param_type = mparams[i].ParameterType;

				if (param_type.FullName == GetTypeName (ctx, types[i])) {
					matchCount++;
					continue;
				}

				if (IsAssignableFrom (ctx, param_type, types[i]))
					continue;

				error = String.Format (
					"Argument {0}: Cannot implicitly convert `{1}' to `{2}'",
					i, GetTypeName (ctx, types[i]), param_type.FullName);
				return false;
			}

			error = null;
			return true;
		}

		Tuple<MethodInfo, CorApi.Portable.Type> OverloadResolve (CorEvaluationContext ctx, string typeName, string methodName, CorApi.Portable.Type[] argtypes, List<Tuple<MethodInfo, CorApi.Portable.Type>> candidates, bool throwIfNotFound)
		{
			if (candidates.Count == 1) {
				string error;
				int matchCount;
				if (IsApplicable (ctx, candidates[0].Item1, argtypes, out error, out matchCount))
					return candidates[0];

				if (throwIfNotFound)
					throw new EvaluatorException ("Invalid arguments for method `{0}': {1}", methodName, error);

				return null;
			}

			if (candidates.Count == 0) {
				if (throwIfNotFound)
					throw new EvaluatorException ("Method `{0}' not found in type `{1}'.", methodName, typeName);

				return null;
			}

			// Ok, now we need to find an exact match.
			Tuple<MethodInfo, CorApi.Portable.Type> match = null;
			int bestCount = -1;
			bool repeatedBestCount = false;

			foreach (var method in candidates) {
				string error;
				int matchCount;

				if (!IsApplicable (ctx, method.Item1, argtypes, out error, out matchCount))
					continue;

				if (matchCount == bestCount) {
					repeatedBestCount = true;
				}
				else if (matchCount > bestCount) {
					match = method;
					bestCount = matchCount;
					repeatedBestCount = false;
				}
			}

			if (match == null) {
				if (!throwIfNotFound)
					return null;

				if (methodName != null)
					throw new EvaluatorException ("Invalid arguments for method `{0}'.", methodName);

				throw new EvaluatorException ("Invalid arguments for indexer.");
			}

			if (repeatedBestCount) {
				// If there is an ambiguous match, just pick the first match. If the user was expecting
				// something else, he can provide more specific arguments

				/*				if (!throwIfNotFound)
									return null;
								if (methodName != null)
									throw new EvaluatorException ("Ambiguous method `{0}'; need to use full name", methodName);
								else
									throw new EvaluatorException ("Ambiguous arguments for indexer.", methodName);
				*/
			}

			return match;
		}


		public override string[] GetImportedNamespaces (EvaluationContext ctx)
		{
			var list = new HashSet<string> ();
			foreach (Type t in GetAllTypes (ctx)) {
				list.Add (t.Namespace);
			}
			var arr = new string[list.Count];
			list.CopyTo (arr);
			return arr;
		}

		public override void GetNamespaceContents (EvaluationContext ctx, string namspace, out string[] childNamespaces, out string[] childTypes)
		{
			var nss = new HashSet<string> ();
			var types = new HashSet<string> ();
			string namspacePrefix = namspace.Length > 0 ? namspace + "." : "";
			foreach (Type t in GetAllTypes (ctx)) {
				if (t.Namespace == namspace || t.Namespace.StartsWith (namspacePrefix, StringComparison.InvariantCulture)) {
					nss.Add (t.Namespace);
					types.Add (t.FullName);
				}
			}

			childNamespaces = new string[nss.Count];
			nss.CopyTo (childNamespaces);

			childTypes = new string [types.Count];
			types.CopyTo (childTypes);
		}

		bool IsAssignableFrom (CorEvaluationContext ctx, Type baseType, CorApi.Portable.Type ctype)
		{
			// the type is method generic parameter, we have to check its constraints, but now we don't have the info about it
			// and assume that any type is assignable to method generic type parameter
			if (baseType is MethodGenericParameter)
				return true;
			string tname = baseType.FullName;
			string ctypeName = GetTypeName (ctx, ctype);
			if (tname == "System.Object")
				return true;

			if (tname == ctypeName)
				return true;

			if (MetadataHelperFunctionsExtensions.CoreTypes.ContainsKey (ctype.CorType))
				return false;

			switch (ctype.CorType) {
				case CorApi.Portable.CorElementType.ElementTypeArray:
				case CorApi.Portable.CorElementType.ElementTypeSzarray:
				case CorApi.Portable.CorElementType.ElementTypeByref:
				case CorApi.Portable.CorElementType.ElementTypePtr:
					return false;
			}

			while (ctype != null) {
				if (GetTypeName (ctx, ctype) == tname)
					return true;
				ctype = ctype.Base;
			}
			return false;
		}

		public override object TryCast (EvaluationContext ctx, object val, object type)
		{
			var ctype = (CorApi.Portable.Type) GetValueType (ctx, val);
            var obj = GetRealObject(ctx, val);
			var referenceValue = obj.CastToReferenceValue ();
			if (referenceValue != null && referenceValue.IsNull)
				return val;

            string tname = GetTypeName(ctx, type);
            string ctypeName = GetValueTypeName (ctx, val);
            if (tname == "System.Object")
                return val;

            if (tname == ctypeName)
                return val;

            if (obj is CorApi.Portable.StringValue)
                return ctypeName == tname ? val : null;

            if (obj is CorApi.Portable.ArrayValue)
                return (ctypeName == tname || ctypeName == "System.Array") ? val : null;

			var genVal = obj as CorApi.Portable.GenericValue;
			if (genVal != null) {
				Type t = Type.GetType(tname);
				try {
					if (t != null && t.IsPrimitive && t != typeof (string)) {
						object pval = genVal.GetValue ();
						try {
							pval = System.Convert.ChangeType (pval, t);
						}
						catch {
							// pval = DynamicCast (pval, t);
							return null;
						}
						return CreateValue (ctx, pval);
					}
					else if (IsEnum (ctx, (CorApi.Portable.Type)type)) {
						return CreateEnum (ctx, (CorApi.Portable.Type)type, val);
					}
				} catch {
				}
			}

            if (obj is CorApi.Portable.ObjectValue)
            {
				var co = (CorApi.Portable.ObjectValue)obj;
				if (IsEnum (ctx, co.ExactType)) {
					ValueReference rval = GetMember (ctx, null, val, "value__");
					return TryCast (ctx, rval.Value, type);
				}

                while (ctype != null)
                {
                    if (GetTypeName(ctx, ctype) == tname)
                        return val;
                    ctype = ctype.Base;
                }
                return null;
            }
            return null;
        }

		public bool IsPointer (CorApi.Portable.Type targetType)
		{
			return targetType.CorType == CorApi.Portable.CorElementType.ElementTypePtr;
		}

		public object CreateEnum (EvaluationContext ctx, CorApi.Portable.Type type, object val)
		{
			object systemEnumType = GetType (ctx, "System.Enum");
			object enumType = CreateTypeObject (ctx, type);
			object[] argTypes = { GetValueType (ctx, enumType), GetValueType (ctx, val) };
			object[] args = { enumType, val };
			return RuntimeInvoke (ctx, systemEnumType, null, "ToObject", argTypes, args);
		}

		public bool IsEnum (EvaluationContext ctx, CorApi.Portable.Type targetType)
		{
			return (targetType.CorType == CorApi.Portable.CorElementType.ElementTypeValuetype || targetType.CorType == CorApi.Portable.CorElementType.ElementTypeClass) && targetType.Base != null && GetTypeName (ctx, targetType.Base) == "System.Enum";
		}

		public override object CreateValue (EvaluationContext gctx, object value)
		{
			CorEvaluationContext ctx = (CorEvaluationContext) gctx;
			if (value is string) {
				return new CorValRef (delegate {
					return ctx.Session.NewString (ctx, (string) value);
				});
			}

			foreach (KeyValuePair<CorApi.Portable.CorElementType, Type> tt in MetadataHelperFunctionsExtensions.CoreTypes) {
				if (tt.Value == value.GetType ()) {
					var val = ctx.Eval.CreateValue (tt.Key, null);
					var gv = val.CastToGenericValue ();
					gv.SetValue (value);
					return new CorValRef (val);
				}
			}
			ctx.WriteDebuggerError (new NotSupportedException (String.Format ("Unable to create value for type: {0}", value.GetType ())));
			return null;
		}

		public override object CreateValue (EvaluationContext ctx, object type, params object[] gargs)
		{
			CorValRef[] args = CastArray<CorValRef> (gargs);
			return new CorValRef (delegate {
				return CreateCorValue (ctx, (CorApi.Portable.Type) type, args);
			});
		}

		public CorApi.Portable.Value CreateCorValue (EvaluationContext ctx, CorApi.Portable.Type type, params CorValRef[] args)
		{
			CorEvaluationContext cctx = (CorEvaluationContext)ctx;
			var vargs = new CorApi.Portable.Value [args.Length];
			var targs = new CorApi.Portable.Type[args.Length];
			for (int n = 0; n < args.Length; n++) {
				vargs [n] = args [n].Val;
				targs [n] = vargs [n].ExactType;
			}
			MethodInfo ctor = null;
			var ctorInfo = OverloadResolve (cctx, ".ctor", type, targs, BindingFlags.Instance | BindingFlags.Public, false);
			if (ctorInfo != null) {
				ctor = ctorInfo.Item1;
			}
			if (ctor == null) {
				//TODO: Remove this if and content when Generic method invocation is fully implemented
				Type t = type.GetTypeInfo (cctx.Session);
				foreach (MethodInfo met in t.GetMethods ()) {
					if (met.IsSpecialName && met.Name == ".ctor") {
						ParameterInfo[] pinfos = met.GetParameters ();
						if (pinfos.Length == 1) {
							ctor = met;
							break;
						}
					}
				}
			}

			if (ctor == null)
				return null;

			CorApi.Portable.Function func = type.Class.Module.GetFunctionFromToken ((uint)ctor.MetadataToken);
			return cctx.RuntimeInvoke (func, type.TypeParameters, null, vargs);
		}

		public override object CreateNullValue (EvaluationContext gctx, object type)
		{
			CorEvaluationContext ctx = (CorEvaluationContext) gctx;
			return new CorValRef (ctx.Eval.CreateValueForType ((CorApi.Portable.Type)type));
		}

		public override ICollectionAdaptor CreateArrayAdaptor (EvaluationContext ctx, object arr)
		{
			CorApi.Portable.Value val = CorObjectAdaptor.GetRealObject (ctx, arr);
			
			if (val is CorApi.Portable.ArrayValue)
				return new ArrayAdaptor (ctx, new CorValRef<CorApi.Portable.ArrayValue> ((CorApi.Portable.ArrayValue) val, () => (CorApi.Portable.ArrayValue) GetRealObject (ctx, arr)));
			return null;
		}
		
		public override IStringAdaptor CreateStringAdaptor (EvaluationContext ctx, object str)
		{
			CorApi.Portable.Value val = CorObjectAdaptor.GetRealObject (ctx, str);
			
			if (val is CorApi.Portable.StringValue)
				return new StringAdaptor (ctx, (CorValRef)str, (CorApi.Portable.StringValue)val);
			return null;
		}

		public static CorApi.Portable.Value GetRealObject (EvaluationContext cctx, object objr)
		{
			if (objr == null)
				return null;

			var corValue = objr as CorApi.Portable.Value;
			if (corValue != null)
				return GetRealObject (cctx, corValue);
			var valRef = objr as CorValRef;
			if (valRef != null)
				return GetRealObject (cctx, valRef.Val);
			return null;
		}

		public static CorApi.Portable.Value GetRealObject (EvaluationContext ctx, CorApi.Portable.Value obj)
		{
			CorEvaluationContext cctx = (CorEvaluationContext) ctx;
			if (obj == null)
				return null;

			try {
				if (obj is CorApi.Portable.StringValue)
					return obj;

				if (obj is CorApi.Portable.GenericValue)
					return obj;

				var arrayVal = obj.CastToArrayValue ();
				if (arrayVal != null)
					return arrayVal;

				var refVal = obj.CastToReferenceValue ();
				if (refVal != null) {
					cctx.Session.WaitUntilStopped ();
					if (refVal.IsNull)
						return refVal;
					else {
						return GetRealObject (cctx, refVal.Dereference ());
					}
				}

				cctx.Session.WaitUntilStopped ();
				var boxVal = obj.CastToBoxValue ();
				if (boxVal != null)
					return Unbox (ctx, boxVal);

				if (obj.ExactType.CorType == CorApi.Portable.CorElementType.ElementTypeString)
					return obj.CastToStringValue ();

				if (MetadataHelperFunctionsExtensions.CoreTypes.ContainsKey ((CorApi.Portable.CorElementType)obj.Type)) {
					var genVal = obj.CastToGenericValue ();
					if (genVal != null)
						return genVal;
				}

				if (!(obj is CorApi.Portable.ObjectValue))
					return obj.CastToObjectValue ();
			}
			catch {
				// Ignore
				throw;
			}
			return obj;
		}

		static CorApi.Portable.Value Unbox (EvaluationContext ctx, CorApi.Portable.BoxValue boxVal)
		{
			CorApi.Portable.ObjectValue bval = boxVal.ObjectW;
			Type ptype = Type.GetType (ctx.Adapter.GetTypeName (ctx, bval.ExactType));
			
			if (ptype != null && ptype.IsPrimitive) {
				ptype = bval.ExactType.GetTypeInfo (((CorEvaluationContext)ctx).Session);
				foreach (FieldInfo field in ptype.GetFields (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
					if (field.Name == "m_value") {
						var val = bval.GetFieldValue (bval.ExactType.Class, (uint)field.MetadataToken);
						val = GetRealObject (ctx, val);
						return val;
					}
				}
			}

			return GetRealObject (ctx, bval);
		}

		public override object GetEnclosingType (EvaluationContext gctx)
		{
			CorEvaluationContext ctx = (CorEvaluationContext) gctx;
			if (ctx.Frame.FrameType != CorApi.Portable.CorFrameType.ILFrame || ctx.Frame.Function == null)
				return null;

			CorApi.Portable.Class cls = ctx.Frame.Function.Class;
			var tpars = new List<CorApi.Portable.Type> ();
			foreach (CorApi.Portable.Type t in ctx.Frame.TypeParameters)
				tpars.Add (t);
			return cls.GetParameterizedType (CorApi.Portable.CorElementType.ElementTypeClass, tpars.ToArray ());
		}

		public override IEnumerable<EnumMember> GetEnumMembers (EvaluationContext ctx, object tt)
		{
			var t = (CorApi.Portable.Type)tt;
			CorEvaluationContext cctx = (CorEvaluationContext)ctx;

			Type type = t.GetTypeInfo (cctx.Session);

			foreach (FieldInfo field in type.GetFields (BindingFlags.Public | BindingFlags.Static)) {
				if (field.IsLiteral && field.IsStatic) {
					object val = field.GetValue (null);
					EnumMember em = new EnumMember ();
					em.Value = (long) System.Convert.ChangeType (val, typeof (long));
					em.Name = field.Name;
					yield return em;
				}
			}
		}

		public override ValueReference GetIndexerReference (EvaluationContext ctx, object target, object[] indices)
		{
			CorEvaluationContext cctx = (CorEvaluationContext)ctx;
			var targetType = GetValueType (ctx, target) as CorApi.Portable.Type;

			CorValRef[] values = new CorValRef[indices.Length];
			var types = new CorApi.Portable.Type[indices.Length];
			for (int n = 0; n < indices.Length; n++) {
				types[n] = (CorApi.Portable.Type) GetValueType (ctx, indices[n]);
				values[n] = (CorValRef) indices[n];
			}

			var candidates = new List<Tuple<MethodInfo, CorApi.Portable.Type>> ();
			List<PropertyInfo> props = new List<PropertyInfo> ();
			var propTypes = new List<CorApi.Portable.Type> ();

			var t = targetType;
			while (t != null) {
				Type type = t.GetTypeInfo (cctx.Session);

				foreach (PropertyInfo prop in type.GetProperties (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
					MethodInfo mi = null;
					try {
						mi = prop.CanRead ? prop.GetGetMethod (true) : null;
					}
					catch {
						// Ignore
					}
					if (mi != null && !mi.IsStatic && mi.GetParameters ().Length > 0) {
						candidates.Add (Tuple.Create (mi, t));
						props.Add (prop);
						propTypes.Add (t);
					}
				}
				if (cctx.Adapter.IsPrimitive (ctx, target))
					break;
				t = t.Base;
			}

			var idx = OverloadResolve (cctx, GetTypeName (ctx, targetType), null, types, candidates, true);
			int i = candidates.IndexOf (idx);

			if (props [i].GetGetMethod (true) == null)
				return null;

			return new PropertyReference (ctx, props[i], (CorValRef)target, propTypes[i], values);
		}

		public override bool HasMember (EvaluationContext ctx, object tt, string memberName, BindingFlags bindingFlags)
		{
			CorEvaluationContext cctx = (CorEvaluationContext) ctx;
			var ct = (CorApi.Portable.Type) tt;

			while (ct != null) {
				Type type = ct.GetTypeInfo (cctx.Session);

				FieldInfo field = type.GetField (memberName, bindingFlags);
				if (field != null)
					return true;

				PropertyInfo prop = type.GetProperty (memberName, bindingFlags);
				if (prop != null) {
					MethodInfo getter = prop.CanRead ? prop.GetGetMethod (bindingFlags.HasFlag (BindingFlags.NonPublic)) : null;
					if (getter != null)
						return true;
				}

				if (bindingFlags.HasFlag (BindingFlags.DeclaredOnly))
					break;

				ct = ct.Base;
			}

			return false;
		}

		protected override IEnumerable<ValueReference> GetMembers (EvaluationContext ctx, object tt, object gval, BindingFlags bindingFlags)
		{
			var subProps = new Dictionary<string, PropertyInfo> ();
			var t = (CorApi.Portable.Type) tt;
			var val = (CorValRef) gval;
			CorApi.Portable.Type realType = null;
			if (gval != null && (bindingFlags & BindingFlags.Instance) != 0)
				realType = GetValueType (ctx, gval) as CorApi.Portable.Type;

			if (t.CorType == CorApi.Portable.CorElementType.ElementTypeClass && t.Class == null)
				yield break;

			CorEvaluationContext cctx = (CorEvaluationContext) ctx;

			// First of all, get a list of properties overriden in sub-types
			while (realType != null && realType != t) {
				Type type = realType.GetTypeInfo (cctx.Session);
				foreach (PropertyInfo prop in type.GetProperties (bindingFlags | BindingFlags.DeclaredOnly)) {
					MethodInfo mi = prop.GetGetMethod (true);
					if (mi == null || mi.GetParameters ().Length != 0 || mi.IsAbstract || !mi.IsVirtual || mi.IsStatic)
						continue;
					if (mi.IsPublic && ((bindingFlags & BindingFlags.Public) == 0))
						continue;
					if (!mi.IsPublic && ((bindingFlags & BindingFlags.NonPublic) == 0))
						continue;
					subProps [prop.Name] = prop;
				}
				realType = realType.Base;
			}

			while (t != null) {
				Type type = t.GetTypeInfo (cctx.Session);

				foreach (FieldInfo field in type.GetFields (bindingFlags)) {
					if (field.IsStatic && ((bindingFlags & BindingFlags.Static) == 0))
						continue;
					if (!field.IsStatic && ((bindingFlags & BindingFlags.Instance) == 0))
						continue;
					if (field.IsPublic && ((bindingFlags & BindingFlags.Public) == 0))
						continue;
					if (!field.IsPublic && ((bindingFlags & BindingFlags.NonPublic) == 0))
						continue;
					yield return new FieldReference (ctx, val, t, field);
				}

				foreach (PropertyInfo prop in type.GetProperties (bindingFlags)) {
					MethodInfo mi = null;
					try {
						mi = prop.CanRead ? prop.GetGetMethod (true) : null;
					} catch {
						// Ignore
					}
					if (mi == null || mi.GetParameters ().Length != 0 || mi.IsAbstract)
						continue;

					if (mi.IsStatic && ((bindingFlags & BindingFlags.Static) == 0))
						continue;
					if (!mi.IsStatic && ((bindingFlags & BindingFlags.Instance) == 0))
						continue;
					if (mi.IsPublic && ((bindingFlags & BindingFlags.Public) == 0))
						continue;
					if (!mi.IsPublic && ((bindingFlags & BindingFlags.NonPublic) == 0))
						continue;

					// If a property is overriden, return the override instead of the base property
					PropertyInfo overridden;
					if (mi.IsVirtual && subProps.TryGetValue (prop.Name, out overridden)) {
						mi = overridden.GetGetMethod (true);
						if (mi == null)
							continue;

						var declaringType = GetType (ctx, overridden.DeclaringType.FullName) as CorApi.Portable.Type;
						yield return new PropertyReference (ctx, overridden, val, declaringType);
					} else {
						yield return new PropertyReference (ctx, prop, val, t);
					}
				}
				if ((bindingFlags & BindingFlags.DeclaredOnly) != 0)
					break;
				t = t.Base;
			}
		}

		static T FindByName<T> (IEnumerable<T> elems, Func<T,string> getName, string name, bool caseSensitive)
		{
			T best = default(T);
			foreach (T t in elems) {
				string n = getName (t);
				if (n == name) 
					return t;
				if (!caseSensitive && n.Equals (name, StringComparison.CurrentCultureIgnoreCase))
					best = t;
			}
			return best;
		}

		static bool IsStatic (PropertyInfo prop)
		{
			MethodInfo met = prop.GetGetMethod (true) ?? prop.GetSetMethod (true);
			return met.IsStatic;
		}

		static bool IsAnonymousType (Type type)
		{
			return type.Name.StartsWith ("<>__AnonType", StringComparison.Ordinal);
		}

		static bool IsCompilerGenerated (FieldInfo field)
		{
			return field.GetCustomAttributes (true).Any (v => v is System.Diagnostics.DebuggerHiddenAttribute);
		}

		protected override ValueReference GetMember (EvaluationContext ctx, object t, object co, string name)
		{
			var cctx = ctx as CorEvaluationContext;
			var type = t as CorApi.Portable.Type;

			if (IsNullableType (ctx, t)) {
				// 'Value' and 'HasValue' property evaluation gives wrong results when the nullable object is a property of class.
				// Replace to direct field access to fix it. Actual cause of this problem is unknown
				switch (name) {
					case "Value":
						name = "value";
						break;
					case "HasValue":
						name = "hasValue";
						break;
				}
			}

			while (type != null) {
				var tt = type.GetTypeInfo (cctx.Session);
				FieldInfo field = FindByName (tt.GetFields (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance), f => f.Name, name, ctx.CaseSensitive);
				if (field != null && (field.IsStatic || co != null))
					return new FieldReference (ctx, co as CorValRef, type, field);

				PropertyInfo prop = FindByName (tt.GetProperties (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance), p => p.Name, name, ctx.CaseSensitive);
				if (prop != null && (IsStatic (prop) || co != null)) {
					// Optimization: if the property has a CompilerGenerated backing field, use that instead.
					// This way we avoid overhead of invoking methods on the debugee when the value is requested.
					string cgFieldName = string.Format ("<{0}>{1}", prop.Name, IsAnonymousType (tt) ? "" : "k__BackingField");
					if ((field = FindByName (tt.GetFields (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance), f => f.Name, cgFieldName, true)) != null && IsCompilerGenerated (field))
						return new FieldReference (ctx, co as CorValRef, type, field, prop.Name, ObjectValueFlags.Property);

					// Backing field not available, so do things the old fashioned way.
					MethodInfo getter = prop.GetGetMethod (true);
					if (getter == null)
						return null;

					return new PropertyReference (ctx, prop, co as CorValRef, type);
				}

				type = type.Base;
			}

			return null;
		}

		static bool IsIEnumerable (Type type)
		{
			if (type.Namespace == "System.Collections" && type.Name == "IEnumerable")
				return true;

			if (type.Namespace == "System.Collections.Generic" && type.Name == "IEnumerable`1")
				return true;

			return false;
		}

		static bool IsIEnumerable (CorApi.Portable.Type type, CorDebuggerSession session)
		{
			return IsIEnumerable (type.GetTypeInfo (session));
		}

		protected override CompletionData GetMemberCompletionData (EvaluationContext ctx, ValueReference vr)
		{
			var properties = new HashSet<string> ();
			var methods = new HashSet<string> ();
			var fields = new HashSet<string> ();
			var data = new CompletionData ();
			var type = vr.Type as CorApi.Portable.Type;
			bool isEnumerable = false;
			Type t;

			var cctx = (CorEvaluationContext)ctx;
			while (type != null) {
				t = type.GetTypeInfo (cctx.Session);
				if (!isEnumerable && IsIEnumerable (t))
					isEnumerable = true;

				foreach (var field in t.GetFields ()) {
					if (field.IsStatic || field.IsSpecialName || !field.IsPublic)
						continue;

					if (fields.Add (field.Name))
						data.Items.Add (new CompletionItem (field.Name, FieldReference.GetFlags (field)));
				}

				foreach (var property in t.GetProperties ()) {
					var getter = property.GetGetMethod (true);

					if (getter == null || getter.IsStatic || !getter.IsPublic)
						continue;

					if (properties.Add (property.Name))
						data.Items.Add (new CompletionItem (property.Name, PropertyReference.GetFlags (property)));
				}

				foreach (var method in t.GetMethods ()) {
					if (method.IsStatic || method.IsConstructor || method.IsSpecialName || !method.IsPublic)
						continue;

					if (methods.Add (method.Name))
						data.Items.Add (new CompletionItem (method.Name, ObjectValueFlags.Method | ObjectValueFlags.Public));
				}

				if (t.BaseType == null && t.FullName != "System.Object")
					type = ctx.Adapter.GetType (ctx, "System.Object") as CorApi.Portable.Type;
				else
					type = type.Base;
			}

			type = vr.Type as CorApi.Portable.Type;
			t = type.GetTypeInfo (cctx.Session);
			foreach (var iface in t.GetInterfaces ()) {
				if (!isEnumerable && IsIEnumerable (iface)) {
					isEnumerable = true;
					break;
				}
			}

			if (isEnumerable) {
				// Look for LINQ extension methods...
				var linq = ctx.Adapter.GetType (ctx, "System.Linq.Enumerable") as CorApi.Portable.Type;
				if (linq != null) {
					var linqt = linq.GetTypeInfo (cctx.Session);
					foreach (var method in linqt.GetMethods ()) {
						if (!method.IsStatic || method.IsConstructor || method.IsSpecialName || !method.IsPublic)
							continue;

						if (methods.Add (method.Name))
							data.Items.Add (new CompletionItem (method.Name, ObjectValueFlags.Method | ObjectValueFlags.Public));
					}
				}
			}

			data.ExpressionLength = 0;

			return data;
		}

		public override object TargetObjectToObject (EvaluationContext ctx, object objr)
		{
			var obj = GetRealObject (ctx, objr);

			if ((obj is CorApi.Portable.ReferenceValue) && ((CorApi.Portable.ReferenceValue)obj).IsNull)
				return null;

			var stringVal = obj as CorApi.Portable.StringValue;
			if (stringVal != null) {
				string str;
				if (ctx.Options.EllipsizeStrings) {
					str = stringVal.String;
					if (str.Length > ctx.Options.EllipsizedLength)
						str = str.Substring (0, ctx.Options.EllipsizedLength) + EvaluationOptions.Ellipsis;
				} else {
					str = stringVal.String;
				}
				return str;

			}

			var arr = obj as CorApi.Portable.ArrayValue;
			if (arr != null)
                return base.TargetObjectToObject(ctx, objr);

			var co = obj as CorApi.Portable.ObjectValue;
			if (co != null)
                return base.TargetObjectToObject(ctx, objr);

			var genVal = obj as CorApi.Portable.GenericValue;
			if (genVal != null)
				return genVal.GetValue ();

			return base.TargetObjectToObject (ctx, objr);
		}

		static bool InGeneratedClosureOrIteratorType (CorEvaluationContext ctx)
		{
			MethodInfo mi = ctx.Frame.Function.GetMethodInfo (ctx.Session);
			if (mi == null || mi.IsStatic)
				return false;

			Type tm = mi.DeclaringType;
			return IsGeneratedType (tm);
		}

		internal static bool IsGeneratedType (string name)
		{
			//
			// This should cover all C# generated special containers
			// - anonymous methods
			// - lambdas
			// - iterators
			// - async methods
			//
			// which allow stepping into
			//

			return name[0] == '<' &&
				// mcs is of the form <${NAME}>.c__{KIND}${NUMBER}
				(name.IndexOf (">c__", StringComparison.Ordinal) > 0 ||
				// csc is of form <${NAME}>d__${NUMBER}
				name.IndexOf (">d__", StringComparison.Ordinal) > 0);
		}

		internal static bool IsGeneratedType (Type tm)
		{
			return IsGeneratedType (tm.Name);
		}

		ValueReference GetHoistedThisReference (CorEvaluationContext cx)
		{
			return CorDebugUtil.CallHandlingComExceptions (() => {
				CorValRef vref = new CorValRef (delegate {
					return cx.Frame.GetArgument (0);
				});
				var type = (CorApi.Portable.Type) GetValueType (cx, vref);
				return GetHoistedThisReference (cx, type, vref);
			}, "GetHoistedThisReference()");
		}

		ValueReference GetHoistedThisReference (CorEvaluationContext cx, CorApi.Portable.Type type, object val)
		{
			Type t = type.GetTypeInfo (cx.Session);
			var vref = (CorValRef)val;
			foreach (FieldInfo field in t.GetFields ()) {
				if (IsHoistedThisReference (field))
					return new FieldReference (cx, vref, type, field, "this", ObjectValueFlags.Literal);

				if (IsClosureReferenceField (field)) {
					var fieldRef = new FieldReference (cx, vref, type, field);
					var fieldType = (CorApi.Portable.Type)GetValueType (cx, fieldRef.Value);
					var thisRef = GetHoistedThisReference (cx, fieldType, fieldRef.Value);
					if (thisRef != null)
						return thisRef;
				}
			}

			return null;
		}

		static bool IsHoistedThisReference (FieldInfo field)
		{
			// mcs is "<>f__this" or "$this" (if in an async compiler generated type)
			// csc is "<>4__this"
			return field.Name == "$this" ||
				(field.Name.StartsWith ("<>", StringComparison.Ordinal) &&
				field.Name.EndsWith ("__this", StringComparison.Ordinal));
		}

		static bool IsClosureReferenceField (FieldInfo field)
		{
			// mcs is "<>f__ref"
			// csc is "CS$<>"
			// roslyn is "<>8__"
			return field.Name.StartsWith ("CS$<>", StringComparison.Ordinal) ||
			field.Name.StartsWith ("<>f__ref", StringComparison.Ordinal) ||
			field.Name.StartsWith ("<>8__", StringComparison.Ordinal);
		}

		static bool IsClosureReferenceLocal (ISymbolVariable local)
		{
			if (local.Name == null)
				return false;

			// mcs is "$locvar" or starts with '<'
			// csc is "CS$<>"
			return local.Name.Length == 0 || local.Name[0] == '<' || local.Name.StartsWith ("$locvar", StringComparison.Ordinal) ||
				local.Name.StartsWith ("CS$<>", StringComparison.Ordinal);
		}

		static bool IsGeneratedTemporaryLocal (ISymbolVariable local)
		{
			// csc uses CS$ prefix for temporary variables and <>t__ prefix for async task-related state variables
			return local.Name != null && (local.Name.StartsWith ("CS$", StringComparison.Ordinal) || local.Name.StartsWith ("<>t__", StringComparison.Ordinal));
		}

		protected override ValueReference OnGetThisReference (EvaluationContext ctx)
		{
			CorEvaluationContext cctx = (CorEvaluationContext) ctx;
			if (cctx.Frame.FrameType != CorApi.Portable.CorFrameType.ILFrame || cctx.Frame.Function == null)
				return null;

			if (InGeneratedClosureOrIteratorType (cctx))
				return GetHoistedThisReference (cctx);

			return GetThisReference (cctx);

		}

		ValueReference GetThisReference (CorEvaluationContext ctx)
		{
			MethodInfo mi = ctx.Frame.Function.GetMethodInfo (ctx.Session);
			if (mi == null || mi.IsStatic)
				return null;

			return CorDebugUtil.CallHandlingComExceptions (() => {
				CorValRef vref = new CorValRef (delegate {
					var result = ctx.Frame.GetArgument (0);
					if (result.Type == CorApi.Portable.CorElementType.ElementTypeByref)
						return result.CastToReferenceValue ().Dereference ();
					return result;
				});

				return new VariableReference (ctx, vref, "this", ObjectValueFlags.Variable | ObjectValueFlags.ReadOnly);
			}, "GetThisReference()");
		}

		static VariableReference CreateParameterReference (CorEvaluationContext ctx, int paramIndex, string paramName, ObjectValueFlags flags = ObjectValueFlags.Parameter)
		{
			CorValRef vref = new CorValRef (delegate {
				return ctx.Frame.GetArgument (paramIndex);
			});
			return new VariableReference (ctx, vref, paramName, flags);
		}


		protected override IEnumerable<ValueReference> OnGetParameters (EvaluationContext gctx)
		{
			CorEvaluationContext ctx = (CorEvaluationContext) gctx;
			if (ctx.Frame.FrameType == CorApi.Portable.CorFrameType.ILFrame && ctx.Frame.Function != null) {
				MethodInfo met = ctx.Frame.Function.GetMethodInfo (ctx.Session);
				if (met != null) {
					foreach (ParameterInfo pi in met.GetParameters ()) {
						int pos = pi.Position;
						if (met.IsStatic)
							pos--;

						var parameter = CorDebugUtil.CallHandlingComExceptions (() => CreateParameterReference (ctx, pos, pi.Name),
							string.Format ("Get parameter {0} of {1}", pi.Name, met.Name));
						if (parameter != null)
							yield return parameter;
					}
					yield break;
				}
			}

			int count = CorDebugUtil.CallHandlingComExceptions (() => ctx.Frame.GetArgumentCount (), "GetArgumentCount()", 0);
			for (int n = 0; n < count; n++) {
				int locn = n;
				var parameter = CorDebugUtil.CallHandlingComExceptions (() => CreateParameterReference (ctx, locn, "arg_" + (locn + 1)),
					string.Format ("Get parameter {0}", n));
				if (parameter != null)
					yield return parameter;
			}
		}

		protected override IEnumerable<ValueReference> OnGetLocalVariables (EvaluationContext ctx)
		{
			CorEvaluationContext cctx = (CorEvaluationContext)ctx;
			if (InGeneratedClosureOrIteratorType (cctx)) {
				ValueReference vthis = GetThisReference (cctx);
				return GetHoistedLocalVariables (cctx, vthis).Union (GetLocalVariables (cctx));
			}

			return GetLocalVariables (cctx);
		}

		IEnumerable<ValueReference> GetHoistedLocalVariables (CorEvaluationContext cx, ValueReference vthis)
		{
			if (vthis == null)
				return new ValueReference [0];

			object val = vthis.Value;
			if (IsNull (cx, val))
				return new ValueReference [0];

			var tm = (CorApi.Portable.Type) vthis.Type;
			Type t = tm.GetTypeInfo (cx.Session);
			bool isIterator = IsGeneratedType (t);

			var list = new List<ValueReference> ();
			foreach (FieldInfo field in t.GetFields (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
				if (IsHoistedThisReference (field))
					continue;
				if (IsClosureReferenceField (field)) {
					list.AddRange (GetHoistedLocalVariables (cx, new FieldReference (cx, (CorValRef)val, tm, field)));
					continue;
				}
				if (field.Name[0] == '<') {
					if (isIterator) {
						var name = GetHoistedIteratorLocalName (field);
						if (!string.IsNullOrEmpty (name)) {
							list.Add (new FieldReference (cx, (CorValRef)val, tm, field, name, ObjectValueFlags.Variable));
						}
					}
				} else if (!field.Name.Contains ("$")) {
					list.Add (new FieldReference (cx, (CorValRef)val, tm, field, field.Name, ObjectValueFlags.Variable));
				}
			}
			return list;
		}

		static string GetHoistedIteratorLocalName (FieldInfo field)
		{
			//mcs captured args, of form <$>name
			if (field.Name.StartsWith ("<$>", StringComparison.Ordinal)) {
				return field.Name.Substring (3);
			}

			// csc, mcs locals of form <name>__0
			if (field.Name[0] == '<') {
				int i = field.Name.IndexOf ('>');
				if (i > 1) {
					return field.Name.Substring (1, i - 1);
				}
			}
			return null;
		}

		IEnumerable<ValueReference> GetLocalVariables (CorEvaluationContext cx)
		{
			int offset;
			CorApi.Portable.CorDebugMappingResult mr;
			return CorDebugUtil.CallHandlingComExceptions (() => {
				cx.Frame.GetIP (out offset, out mr);
				return GetLocals (cx, null, (int) offset, false);
			}, "GetLocalVariables()", new ValueReference[0]);
		}
		
		public override ValueReference GetCurrentException (EvaluationContext ctx)
		{
			CorEvaluationContext wctx = (CorEvaluationContext) ctx;
			var exception = wctx.Thread.CurrentException;

			if (exception == null)
				return null;
			return CorDebugUtil.CallHandlingComExceptions (() => {
				CorValRef vref = new CorValRef (delegate {
					return wctx.Session.GetHandle (exception);
				});
				return new VariableReference (ctx, vref, ctx.Options.CurrentExceptionTag, ObjectValueFlags.Variable);
			}, "Get current exception");
		}

		static VariableReference CreateLocalVariableReference (CorEvaluationContext ctx, int varIndex, string varName, ObjectValueFlags flags = ObjectValueFlags.Variable)
		{
			CorValRef vref = new CorValRef (delegate {
				return ctx.Frame.GetLocalVariable (varIndex);
			});
			return new VariableReference (ctx, vref, varName, flags);
		}

		IEnumerable<ValueReference> GetLocals (CorEvaluationContext ctx, ISymbolScope scope, int offset, bool showHidden)
		{
			if (ctx.Frame.FrameType != CorApi.Portable.CorFrameType.ILFrame)
				yield break;

			if (scope == null) {
				ISymbolMethod met = ctx.Frame.Function.GetSymbolMethod (ctx.Session);
				if (met != null)
					scope = met.RootScope;
				else {
					int count = ctx.Frame.GetLocalVariablesCount ();
					for (int n = 0; n < count; n++) {
						int locn = n;
						var localVar = CorDebugUtil.CallHandlingComExceptions (() => CreateLocalVariableReference (ctx, locn, "local_" + (locn + 1)),
							string.Format ("Get local variable {0}", locn));
						if (localVar != null)
							yield return localVar;
					}
					yield break;
				}
			}

			foreach (ISymbolVariable var in scope.GetLocals ()) {
				if (var.Name == "$site")
					continue;
				if (IsClosureReferenceLocal (var)) {
					var variableReference = CorDebugUtil.CallHandlingComExceptions (() => {
						int addr = var.AddressField1;
						return CreateLocalVariableReference (ctx, addr, var.Name);
					}, string.Format ("Get local variable {0}", var.Name));

					if (variableReference != null) {
						foreach (var gv in GetHoistedLocalVariables (ctx, variableReference)) {
							yield return gv;
						}
					}
				} else if (!IsGeneratedTemporaryLocal (var) || showHidden) {
					var variableReference = CorDebugUtil.CallHandlingComExceptions (() => {
						int addr = var.AddressField1;
						return CreateLocalVariableReference (ctx, addr, var.Name);
					}, string.Format ("Get local variable {0}", var.Name));
					if (variableReference != null)
						yield return variableReference;
				}
			}

			foreach (ISymbolScope cs in scope.GetChildren ()) {
				if (cs.StartOffset <= offset && cs.EndOffset >= offset) {
					foreach (ValueReference var in GetLocals (ctx, cs, offset, showHidden))
						yield return var;
				}
			}
		}

		protected override TypeDisplayData OnGetTypeDisplayData (EvaluationContext ctx, object gtype)
		{
			var type = (CorApi.Portable.Type) gtype;

			var wctx = (CorEvaluationContext) ctx;
			Type t = type.GetTypeInfo (wctx.Session);
			if (t == null)
				return null;

			string proxyType = null;
			string nameDisplayString = null;
			string typeDisplayString = null;
			string valueDisplayString = null;
			Dictionary<string, DebuggerBrowsableState> memberData = null;
			bool hasTypeData = false;
			bool isCompilerGenerated = false;

			try {
				foreach (object att in t.GetCustomAttributes (false)) {
					DebuggerTypeProxyAttribute patt = att as DebuggerTypeProxyAttribute;
					if (patt != null) {
						proxyType = patt.ProxyTypeName;
						hasTypeData = true;
						continue;
					}
					DebuggerDisplayAttribute datt = att as DebuggerDisplayAttribute;
					if (datt != null) {
						hasTypeData = true;
						nameDisplayString = datt.Name;
						typeDisplayString = datt.Type;
						valueDisplayString = datt.Value;
						continue;
					}
					CompilerGeneratedAttribute cgatt = att as CompilerGeneratedAttribute;
					if (cgatt != null) {
						isCompilerGenerated = true;
						continue;
					}
				}

				ArrayList mems = new ArrayList ();
				mems.AddRange (t.GetFields (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance));
				mems.AddRange (t.GetProperties (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance));

				foreach (MemberInfo m in mems) {
					object[] atts = m.GetCustomAttributes (typeof (DebuggerBrowsableAttribute), false);
					if (atts.Length > 0) {
						hasTypeData = true;
						if (memberData == null)
							memberData = new Dictionary<string, DebuggerBrowsableState> ();
						memberData[m.Name] = ((DebuggerBrowsableAttribute)atts[0]).State;
					}
				}
			} catch (Exception ex) {
				DebuggerLoggingService.LogError ("Exception in OnGetTypeDisplayData()", ex);
			}
			if (hasTypeData)
				return new TypeDisplayData (proxyType, valueDisplayString, typeDisplayString, nameDisplayString, isCompilerGenerated, memberData);
			else
				return null;
		}

		public override IEnumerable<object> GetNestedTypes (EvaluationContext ctx, object type)
		{
			var cType = (CorApi.Portable.Type)type;
			var wctx = (CorEvaluationContext)ctx;
			var mod = cType.Class.Module;
			uint token = cType.Class.Token;
			var module = wctx.Session.GetMetadataForModule (mod);
			foreach (var t in module.DefinedTypes) {
				if (((MetadataType)t).DeclaringType != null && ((MetadataType)t).DeclaringType.MetadataToken == token) {
					var cls = mod.GetClassFromToken ((uint)((MetadataType)t).MetadataToken);
					var returnType = cls.GetParameterizedType (CorApi.Portable.CorElementType.ElementTypeClass, new CorApi.Portable.Type[0]);
					if (!IsGeneratedType (returnType.GetTypeInfo (wctx.Session))) {
						yield return returnType;
					}
				}
			}
		}

		public override IEnumerable<object> GetImplementedInterfaces (EvaluationContext ctx, object type)
		{
			var cType = (CorApi.Portable.Type)type;
			var typeInfo = cType.GetTypeInfo (((CorEvaluationContext)ctx).Session);
			foreach (var iface in typeInfo.GetInterfaces ()) {
				if (!string.IsNullOrEmpty (iface.FullName))
					yield return GetType (ctx, iface.FullName);
			}
		}

		// TODO: implement for session
		public override bool IsExternalType (EvaluationContext ctx, object type)
		{
			return base.IsExternalType (ctx, type);
		}

		public override bool IsTypeLoaded (EvaluationContext ctx, string typeName)
		{
			return ctx.Adapter.GetType (ctx, typeName) != null;
		}

		public override bool IsTypeLoaded (EvaluationContext ctx, object type)
		{
			return IsTypeLoaded (ctx, GetTypeName (ctx, type));
		}
		// TODO: Implement GetHoistedLocalVariables
	}
}
