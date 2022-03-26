﻿using System;
using System.Reflection;
using System.Reflection.Emit;

namespace BenchmarkDotNet.Helpers.Reflection.Emit
{
    internal static class IlGeneratorStatementExtensions
    {
        public static void EmitCallBaseParameterlessCtor(this ILGenerator ilGenerator, ConstructorBuilder constructorBuilder)
        {
            if (constructorBuilder.IsStatic)
                throw new ArgumentException($"Can not emit base call for {constructorBuilder}");
            if (constructorBuilder.DeclaringType == null)
                throw new ArgumentException($"The {nameof(constructorBuilder)} should have non-null {nameof(constructorBuilder.DeclaringType)}.");

            /*
                IL_0000: ldarg.0
                IL_0001: call instance void [BenchmarkDotNet]BenchmarkDotNet.Samples.SampleBenchmark::.ctor()
            */
            var baseCtor = constructorBuilder.DeclaringType.BaseType?.GetConstructor(Array.Empty<Type>());
            if (baseCtor == null)
                throw new ArgumentException(
                    $"Base type of {constructorBuilder.DeclaringType} has no public parameterless constructor.");

            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Call, baseCtor);
        }

        public static void EmitCtorReturn(this ILGenerator ilBuilder, ConstructorBuilder methodBuilder)
        {
            // IL_0000: ret
            ilBuilder.Emit(OpCodes.Ret);
        }

        public static void EmitVoidReturn(this ILGenerator ilBuilder, MethodBuilder methodBuilder)
        {
            if (methodBuilder.ReturnType != typeof(void))
                throw new InvalidOperationException(
                    $"[BUG] Attempt to emit empty return for non-void method {methodBuilder.Name}.");

            // IL_0000: ret
            ilBuilder.Emit(OpCodes.Ret);
        }

        public static void EmitSetDelegateToThisField(
            this ILGenerator ilBuilder,
            FieldBuilder delegateField,
            MethodInfo delegateMethod)
        {
            if (delegateField.IsStatic)
                throw new ArgumentException("The field should be instance field", nameof(delegateField));

            if (delegateMethod != null)
            {
                /*
                    // globalSetupAction = base.GlobalSetup; // instance
                    IL_0006: ldarg.0
                    IL_0007: ldarg.0
                    IL_0008: ldftn instance void [BenchmarkDotNet]BenchmarkDotNet.Samples.SampleBenchmark::GlobalSetup()
                    IL_000e: newobj instance void [mscorlib]System.Action::.ctor(object, native int)
                    IL_0013: stfld class [mscorlib]System.Action BenchmarkDotNet.Autogenerated.Runnable_0::globalSetupAction
                    // globalCleanupAction = BaseClass.GlobalCleanup; // static
                    IL_0018: ldarg.0
                    IL_0019: ldnull
                    IL_001a: ldftn void [BenchmarkDotNet]BenchmarkDotNet.Samples.SampleBenchmark::GlobalCleanup()
                    IL_0020: newobj instance void [mscorlib]System.Action::.ctor(object, native int)
                    IL_0025: stfld class [mscorlib]System.Action BenchmarkDotNet.Autogenerated.Runnable_0::globalCleanupAction
                 */
                var delegateCtor = delegateField.FieldType.GetConstructor(new[] { typeof(object), typeof(IntPtr) });
                if (delegateCtor == null)
                    throw new InvalidOperationException($"Bug: field {delegateField.Name} is not a delegate field.");

                ilBuilder.Emit(OpCodes.Ldarg_0);
                ilBuilder.Emit(delegateMethod.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0);
                ilBuilder.Emit(OpCodes.Ldftn, delegateMethod);
                ilBuilder.Emit(OpCodes.Newobj, delegateCtor);
                ilBuilder.Emit(OpCodes.Stfld, delegateField);
            }
            else
            {
                ilBuilder.Emit(OpCodes.Ldarg_0);
                ilBuilder.Emit(OpCodes.Ldnull);
                ilBuilder.Emit(OpCodes.Stfld, delegateField);
            }
        }

        public static void EmitLoopBeginFromLocToArg(
            this ILGenerator ilBuilder,
            Label loopStartLabel,
            Label loopHeadLabel,
            LocalBuilder indexLocal,
            ParameterInfo toArg)
        {
            // loop counter stored as loc0, loop max passed as arg1
            /*
                // for (long i = 0L; i < invokeCount; i++)
                IL_0000: ldc.i4.0
                IL_0001: conv.i8
                IL_0002: stloc.0
            */
            ilBuilder.Emit(OpCodes.Ldc_I4_0);
            ilBuilder.Emit(OpCodes.Conv_I8);
            ilBuilder.EmitStloc(indexLocal);

            // IL_0003: br.s IL_0036 // loop head: IL_0036 // we use long jump
            ilBuilder.Emit(OpCodes.Br, loopHeadLabel);

            // loop start (head: IL_0036)
            ilBuilder.MarkLabel(loopStartLabel);
        }

        public static void EmitLoopEndFromLocToArg(
            this ILGenerator ilBuilder,
            Label loopStartLabel,
            Label loopHeadLabel,
            LocalBuilder indexLocal,
            ParameterInfo toArg)
        {
            // loop counter stored as loc0, loop max passed as arg1
            /*
                // for (long i = 0L; i < invokeCount; i++)
                IL_0031: ldloc.0
                IL_0032: ldc.i4.1
                IL_0033: conv.i8
                IL_0034: add
                IL_0035: stloc.0
             */
            ilBuilder.EmitLdloc(indexLocal);
            ilBuilder.Emit(OpCodes.Ldc_I4_1);
            ilBuilder.Emit(OpCodes.Conv_I8);
            ilBuilder.Emit(OpCodes.Add);
            ilBuilder.EmitStloc(indexLocal);

            /*
                // for (long i = 0L; i < invokeCount; i++)
                IL_0036: ldloc.0 // loop head: IL_0036
                IL_0037: ldarg.1
                IL_0038: blt.s IL_0005 // we use long jump
                // end loop
             */
            ilBuilder.MarkLabel(loopHeadLabel);
            ilBuilder.EmitLdloc(indexLocal);
            ilBuilder.EmitLdarg(toArg);
            ilBuilder.Emit(OpCodes.Blt, loopStartLabel);
        }

        public static void EmitLoopBeginFromFldTo0(
            this ILGenerator ilBuilder,
            Label loopStartLabel,
            Label loopHeadLabel)
        {
            // IL_001b: br.s IL_0029 // loop start (head: IL_0029)
            ilBuilder.Emit(OpCodes.Br, loopHeadLabel);

            // loop start (head: IL_0036)
            ilBuilder.MarkLabel(loopStartLabel);
        }

        public static void EmitLoopEndFromFldTo0(
            this ILGenerator ilBuilder,
            Label loopStartLabel,
            Label loopHeadLabel,
            FieldBuilder counterField,
            LocalBuilder counterLocal)
        {
            // loop counter stored as loc0, loop max passed as arg1
            /*
                // while (--repeatsRemaining >= 0)
                IL_0029: ldarg.0
                IL_002a: ldarg.0
                IL_002b: ldfld int64 BenchmarkRunner_0::repeatsRemaining
                IL_0030: ldc.i4.1
                IL_0031: conv.i8
                IL_0032: sub
                IL_0033: stloc.1
                IL_0034: ldloc.1
                IL_0035: stfld int64 BenchmarkRunner_0::repeatsRemaining
                IL_003a: ldloc.1
                IL_003b: ldc.i4.0
                IL_003c: conv.i8
                IL_003d: bge.s IL_001d
                // end loop
             */
            ilBuilder.MarkLabel(loopHeadLabel);
            ilBuilder.Emit(OpCodes.Ldarg_0);
            ilBuilder.Emit(OpCodes.Ldarg_0);
            ilBuilder.Emit(OpCodes.Ldfld, counterField);
            ilBuilder.Emit(OpCodes.Ldc_I4_1);
            ilBuilder.Emit(OpCodes.Conv_I8);
            ilBuilder.Emit(OpCodes.Sub);
            ilBuilder.EmitStloc(counterLocal);
            ilBuilder.EmitLdloc(counterLocal);
            ilBuilder.Emit(OpCodes.Stfld, counterField);
            ilBuilder.EmitLdloc(counterLocal);
            ilBuilder.Emit(OpCodes.Ldc_I4_0);
            ilBuilder.Emit(OpCodes.Conv_I8);
            ilBuilder.Emit(OpCodes.Bge_S, loopStartLabel);
        }
    }
}