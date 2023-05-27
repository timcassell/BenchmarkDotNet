﻿using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Extensions;
using BenchmarkDotNet.Helpers;
using BenchmarkDotNet.Running;

namespace BenchmarkDotNet.Code
{
    internal abstract class DeclarationsProvider
    {
        // "GlobalSetup" or "GlobalCleanup" methods are optional, so default to an empty delegate, so there is always something that can be invoked
        private const string EmptyAction = "() => { }";

        protected readonly Descriptor Descriptor;

        internal DeclarationsProvider(Descriptor descriptor) => Descriptor = descriptor;

        public string OperationsPerInvoke => Descriptor.OperationsPerInvoke.ToString();

        public string WorkloadTypeName => Descriptor.Type.GetCorrectCSharpTypeName();

        public string GlobalSetupMethodName => GetMethodName(Descriptor.GlobalSetupMethod);

        public string GlobalCleanupMethodName => GetMethodName(Descriptor.GlobalCleanupMethod);

        public string IterationSetupMethodName => Descriptor.IterationSetupMethod?.Name ?? EmptyAction;

        public string IterationCleanupMethodName => Descriptor.IterationCleanupMethod?.Name ?? EmptyAction;

        public abstract string ReturnsDefinition { get; }

        protected virtual Type WorkloadMethodReturnType => Descriptor.WorkloadMethod.ReturnType;

        public virtual string WorkloadMethodReturnTypeName => WorkloadMethodReturnType.GetCorrectCSharpTypeName();

        public virtual string WorkloadMethodDelegate(string passArguments) => Descriptor.WorkloadMethod.Name;

        public virtual string WorkloadMethodReturnTypeModifiers => null;

        public virtual string GetWorkloadMethodCall(string passArguments) => $"{Descriptor.WorkloadMethod.Name}({passArguments})";

        public virtual string ConsumeField => null;


        public abstract string OverheadImplementation { get; }

        private string GetMethodName(MethodInfo method)
        {
            if (method == null)
            {
                return EmptyAction;
            }

            if (method.ReturnType == typeof(Task) ||
                method.ReturnType == typeof(ValueTask) ||
                (method.ReturnType.IsGenericType &&
                    (method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>) ||
                     method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))))
            {
                return $"() => {method.Name}().GetAwaiter().GetResult()";
            }

            return method.Name;
        }
    }

    internal class VoidDeclarationsProvider : DeclarationsProvider
    {
        public VoidDeclarationsProvider(Descriptor descriptor) : base(descriptor) { }

        public override string ReturnsDefinition => "RETURNS_VOID";

        public override string OverheadImplementation => string.Empty;
    }

    internal class NonVoidDeclarationsProvider : DeclarationsProvider
    {
        public NonVoidDeclarationsProvider(Descriptor descriptor) : base(descriptor) { }

        public override string ConsumeField
            => !Consumer.IsConsumable(WorkloadMethodReturnType) && Consumer.HasConsumableField(WorkloadMethodReturnType, out var field)
                ? $".{field.Name}"
                : null;

        public override string OverheadImplementation
        {
            get
            {
                var type = WorkloadMethodReturnType;
                // ByRefLike types and pointers use default, everything else uses Unsafe.SkipInit.
                if (type.IsByRefLike() || type.IsPointer)
                {
                    return $"return default({type.GetCorrectCSharpTypeName()});";
                }
                return $"System.Runtime.CompilerServices.Unsafe.SkipInit(out {type.GetCorrectCSharpTypeName()} value);\nreturn value;";
            }
        }

        public override string ReturnsDefinition
            => Consumer.IsConsumable(WorkloadMethodReturnType) || Consumer.HasConsumableField(WorkloadMethodReturnType, out _)
                ? "RETURNS_CONSUMABLE"
                : "RETURNS_NON_CONSUMABLE_STRUCT";
    }

    internal class ByRefDeclarationsProvider : NonVoidDeclarationsProvider
    {
        public ByRefDeclarationsProvider(Descriptor descriptor) : base(descriptor) { }

        public override string WorkloadMethodReturnTypeName => base.WorkloadMethodReturnTypeName.Replace("&", string.Empty);

        public override string ConsumeField => null;

        public override string OverheadImplementation => $"return ref overheadDefaultValueHolder;";

        public override string ReturnsDefinition => "RETURNS_BYREF";

        public override string WorkloadMethodReturnTypeModifiers => "ref";
    }

    internal class ByReadOnlyRefDeclarationsProvider : ByRefDeclarationsProvider
    {
        public ByReadOnlyRefDeclarationsProvider(Descriptor descriptor) : base(descriptor) { }

        public override string WorkloadMethodReturnTypeModifiers => "ref readonly";
    }

    internal class TaskDeclarationsProvider : VoidDeclarationsProvider
    {
        public TaskDeclarationsProvider(Descriptor descriptor) : base(descriptor) { }

        // we use GetAwaiter().GetResult() because it's fastest way to obtain the result in blocking way,
        // and will eventually throw actual exception, not aggregated one
        public override string WorkloadMethodDelegate(string passArguments)
            => $"({passArguments}) => {{ {Descriptor.WorkloadMethod.Name}({passArguments}).GetAwaiter().GetResult(); }}";

        public override string GetWorkloadMethodCall(string passArguments) => $"{Descriptor.WorkloadMethod.Name}({passArguments}).GetAwaiter().GetResult()";

        protected override Type WorkloadMethodReturnType => typeof(void);
    }

    /// <summary>
    /// declarations provider for <see cref="Task{TResult}" /> and <see cref="ValueTask{TResult}" />
    /// </summary>
    internal class GenericTaskDeclarationsProvider : NonVoidDeclarationsProvider
    {
        public GenericTaskDeclarationsProvider(Descriptor descriptor) : base(descriptor) { }

        protected override Type WorkloadMethodReturnType => Descriptor.WorkloadMethod.ReturnType.GetTypeInfo().GetGenericArguments().Single();

        // we use GetAwaiter().GetResult() because it's fastest way to obtain the result in blocking way,
        // and will eventually throw actual exception, not aggregated one
        public override string WorkloadMethodDelegate(string passArguments)
            => $"({passArguments}) => {{ return {Descriptor.WorkloadMethod.Name}({passArguments}).GetAwaiter().GetResult(); }}";

        public override string GetWorkloadMethodCall(string passArguments) => $"{Descriptor.WorkloadMethod.Name}({passArguments}).GetAwaiter().GetResult()";
    }
}