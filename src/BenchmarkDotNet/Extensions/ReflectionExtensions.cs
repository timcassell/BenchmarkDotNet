﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace BenchmarkDotNet.Extensions
{
    internal static class ReflectionExtensions
    {
        internal static T? ResolveAttribute<T>(this Type? type) where T : Attribute =>
            type?.GetTypeInfo().GetCustomAttributes(typeof(T), false).OfType<T>().FirstOrDefault();

        internal static T? ResolveAttribute<T>(this MemberInfo? memberInfo) where T : Attribute =>
            memberInfo?.GetCustomAttributes(typeof(T), false).FirstOrDefault() as T;

        internal static bool HasAttribute<T>(this MemberInfo? memberInfo) where T : Attribute =>
            memberInfo.ResolveAttribute<T>() != null;

        internal static bool IsNullable(this Type type) => Nullable.GetUnderlyingType(type) != null;

        public static bool IsInitOnly(this PropertyInfo propertyInfo)
        {
            var setMethodReturnParameter = propertyInfo.SetMethod?.ReturnParameter;
            if (setMethodReturnParameter == null)
                return false;

            var isExternalInitType = typeof(System.Runtime.CompilerServices.Unsafe).Assembly
                .GetType("System.Runtime.CompilerServices.IsExternalInit");
            if (isExternalInitType == null)
                return false;

            return setMethodReturnParameter.GetRequiredCustomModifiers().Contains(isExternalInitType);
        }

        /// <summary>
        /// returns type name which can be used in generated C# code
        /// </summary>
        internal static string GetCorrectCSharpTypeName(this Type type, bool includeNamespace = true, bool includeGenericArgumentsNamespace = true)
        {
            while (!(type.IsPublic || type.IsNestedPublic) && type.BaseType != null)
                type = type.BaseType;

            // the reflection is missing information about types passed by ref (ie ref ValueTuple<int> is reported as NON generic type)
            if (type.IsByRef && !type.IsGenericType)
                type = type.GetElementType() ?? throw new NullReferenceException(nameof(type.GetElementType)); // https://github.com/dotnet/corefx/issues/29975#issuecomment-393134330

            if (type == typeof(void))
                return "void";
            if (type == typeof(void*))
                return "void*";
            string prefix = "";
            if (!string.IsNullOrEmpty(type.Namespace) && includeNamespace)
                prefix += type.Namespace + ".";

            string nestedTypes = "";
            Type child = type, parent = type.DeclaringType;
            while (child.IsNested && parent != null)
            {
                nestedTypes = parent.Name + "." + nestedTypes;

                child = parent;
                parent = parent.DeclaringType;
            }
            prefix += nestedTypes;


            if (type.GetTypeInfo().IsGenericParameter)
                return type.Name;
            if (type.GetTypeInfo().IsGenericType)
            {
                string mainName = type.Name.Substring(0, type.Name.IndexOf('`'));
                string args = string.Join(", ", type.GetGenericArguments().Select(T => GetCorrectCSharpTypeName(T, includeGenericArgumentsNamespace, includeGenericArgumentsNamespace)).ToArray());
                return $"{prefix}{mainName}<{args}>";
            }

            if (type.IsArray)
                return GetCorrectCSharpTypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";

            return prefix + type.Name.Replace("&", string.Empty);
        }

        /// <summary>
        /// returns simple, human friendly display name
        /// </summary>
        internal static string GetDisplayName(this Type type) => GetDisplayName(type.GetTypeInfo());

        /// <summary>
        /// returns simple, human friendly display name
        /// </summary>
        private static string GetDisplayName(this TypeInfo typeInfo)
        {
            if (!typeInfo.IsGenericType)
                return typeInfo.Name;

            string mainName = typeInfo.Name.Substring(0, typeInfo.Name.IndexOf('`'));
            string args = string.Join(", ", typeInfo.GetGenericArguments().Select(GetDisplayName).ToArray());
            return $"{mainName}<{args}>";
        }

        internal static IEnumerable<MethodInfo> GetAllMethods(this Type type)
        {
            var typeInfo = type.GetTypeInfo();
            while (typeInfo != null)
            {
                foreach (var methodInfo in typeInfo.DeclaredMethods)
                    yield return methodInfo;
                typeInfo = typeInfo.BaseType?.GetTypeInfo();
            }
        }

        internal static IEnumerable<FieldInfo> GetAllFields(this Type type)
        {
            var typeInfo = type.GetTypeInfo();
            while (typeInfo != null)
            {
                foreach (var fieldInfo in typeInfo.DeclaredFields)
                    yield return fieldInfo;
                typeInfo = typeInfo.BaseType?.GetTypeInfo();
            }
        }

        internal static IEnumerable<PropertyInfo> GetAllProperties(this Type type)
        {
            var typeInfo = type.GetTypeInfo();
            while (typeInfo != null)
            {
                foreach (var propertyInfo in typeInfo.DeclaredProperties)
                    yield return propertyInfo;
                typeInfo = typeInfo.BaseType?.GetTypeInfo();
            }
        }

        internal static Type[] GetRunnableBenchmarks(this Assembly assembly)
            => assembly
                .GetTypes()
                .Where(type => type.ContainsRunnableBenchmarks())
                .OrderBy(t => t.Namespace)
                .ThenBy(t => t.Name)
                .ToArray();

        internal static bool ContainsRunnableBenchmarks(this Type type)
        {
            var typeInfo = type.GetTypeInfo();

            if (typeInfo.IsAbstract
                || typeInfo.IsSealed
                || typeInfo.IsNotPublic
                || typeInfo.IsGenericType && !IsRunnableGenericType(typeInfo))
                return false;

            return typeInfo.GetBenchmarks().Any();
        }

        private static MethodInfo[] GetBenchmarks(this TypeInfo typeInfo)
            => typeInfo
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static) // we allow for Static now to produce a nice Validator warning later
                .Where(method => method.GetCustomAttributes(true).OfType<BenchmarkAttribute>().Any())
                .ToArray();

        internal static (string Name, TAttribute Attribute, bool IsStatic, Type ParameterType)[]
            GetTypeMembersWithGivenAttribute<TAttribute>(this Type type, BindingFlags reflectionFlags) where TAttribute : Attribute
        {
            var allFields = type
                .GetFields(reflectionFlags)
                .Select(f => (
                    Name: f.Name,
                    Attribute: f.ResolveAttribute<TAttribute>(),
                    IsStatic: f.IsStatic,
                    ParameterType: f.FieldType));

            var allProperties = type
                .GetProperties(reflectionFlags)
                .Select(p => (
                    Name: p.Name,
                    Attribute: p.ResolveAttribute<TAttribute>(),
                    IsStatic: p.GetSetMethod() != null && p.GetSetMethod().IsStatic,
                    PropertyType: p.PropertyType));

            return allFields.Concat(allProperties).Where(member => member.Attribute != null).ToArray();
        }

        internal static bool IsStackOnlyWithImplicitCast(this Type argumentType, object? argumentInstance)
        {
            if (argumentInstance == null)
                return false;

            if (!argumentType.IsByRefLike())
                return false;

            var instanceType = argumentInstance.GetType();

            var implicitCastsDefinedInArgumentInstance = instanceType.GetMethods().Where(method => method.Name == "op_Implicit" && method.GetParameters().Any()).ToArray();
            if (implicitCastsDefinedInArgumentInstance.Any(implicitCast => implicitCast.ReturnType == argumentType && implicitCast.GetParameters().All(p => p.ParameterType == instanceType)))
                return true;

            var implicitCastsDefinedInArgumentType = argumentType.GetMethods().Where(method => method.Name == "op_Implicit" && method.GetParameters().Any()).ToArray();
            if (implicitCastsDefinedInArgumentType.Any(implicitCast => implicitCast.ReturnType == argumentType && implicitCast.GetParameters().All(p => p.ParameterType == instanceType)))
                return true;

            return false;
        }

        private static bool IsRunnableGenericType(TypeInfo typeInfo)
            => // if it is an open generic - there must be GenericBenchmark attributes
                (!typeInfo.IsGenericTypeDefinition || typeInfo.GenericTypeArguments.Any() || typeInfo.GetCustomAttributes(true).OfType<GenericTypeArgumentsAttribute>().Any())
                    && typeInfo.DeclaredConstructors.Any(ctor => ctor.IsPublic && ctor.GetParameters().Length == 0); // we need public parameterless ctor to create it

        internal static bool IsLinqPad(this Assembly assembly) => assembly.FullName.IndexOf("LINQPAD", StringComparison.OrdinalIgnoreCase) >= 0;

        internal static bool IsByRefLike(this Type type)
            // Type.IsByRefLike is not available in netstandard2.0.
            => type.IsValueType && type.CustomAttributes.Any(attr => attr.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute");

        // Struct size of 64 bytes was observed to be the point at which `default` may be slower in classic Mono, from benchmarks.
        // Between 64 and 128 bytes, both methods may be about the same speed, depending on the complexity of the struct.
        // For all types > 128 bytes, reading from a field is faster than `default`.
        private const int MonoDefaultCutoffSize = 64;

        // We use the fastest possible method to return a value of the workload return type in order to prevent the overhead method from taking longer than the workload method.
        // Classic Mono runs `default` slower than reading a field for very large structs. `default` is faster for all types in all other runtimes.
        internal static bool IsDefaultFasterThanField(this Type type, bool isClassicMono)
            => !isClassicMono || type.SizeOfDefault() <= MonoDefaultCutoffSize;

        private static int SizeOfDefault(this Type type) => type switch
        {
            _ when type == typeof(byte) || type == typeof(sbyte)
                => 1,

            _ when type == typeof(short) || type == typeof(ushort) || type == typeof(char)
                => 2,

            _ when type == typeof(int) || type == typeof(uint)
                => 4,

            _ when type == typeof(long) || type == typeof(ulong)
                => 8,

            _ when type.IsPointer || type.IsClass || type.IsInterface || type == typeof(IntPtr) || type == typeof(UIntPtr)
                => IntPtr.Size,

            _ when type.IsEnum
                => Enum.GetUnderlyingType(type).SizeOfDefault(),

            // Note: the runtime pads structs for alignment purposes, and it enforces a minimum of 1 byte, even for empty structs,
            // but we don't need to worry about either of those cases for the purpose this serves (calculating whether to use `default` or read a field in Mono for the overhead method).
            _ when type.IsValueType
                => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Aggregate(0, (count, field) => field.FieldType.SizeOfDefault() + count),

            _ => throw new Exception("Unknown type size: " + type.FullName)
        };
    }
}