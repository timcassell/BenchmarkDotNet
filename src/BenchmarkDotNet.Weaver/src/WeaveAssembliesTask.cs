using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;

namespace BenchmarkDotNet.Weaver;

internal class CustomAssemblyResolver : DefaultAssemblyResolver
{
    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        // NetStandard causes StackOverflow. https://github.com/jbevain/cecil/issues/573
        // Mscorlib fails to resolve in Visual Studio. https://github.com/jbevain/cecil/issues/966
        // We don't care about any types from these assemblies anyway, so just skip resolving them.
        => name.Name is "netstandard" or "mscorlib" or "System.Runtime" or "System.Private.CoreLib"
            ? null// AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), Path.ChangeExtension(name.Name, ".dll")))
            : base.Resolve(name, parameters);
}

/// <summary>
/// The Task used by MSBuild to weave the assemblies.
/// </summary>
public sealed class WeaveAssembliesTask : Task
{
    /// <summary>
    /// The directory of the output.
    /// </summary>
    [Required]
    public string TargetDir { get; set; }

    /// <summary>
    /// The path of the target assemblies.
    /// </summary>
    [Required]
    public string TargetAssembly { get; set; }

    private List<string> _warningMessages;

    /// <summary>
    /// Runs the weave assemblies task.
    /// </summary>
    /// <returns><see langword="true"/> if successful; <see langword="false"/> otherwise.</returns>
    public override bool Execute()
    {
        if (!File.Exists(TargetAssembly))
        {
            Log.LogError($"Assembly not found: {TargetAssembly}");
            return false;
        }

        var resolver = new CustomAssemblyResolver();
        resolver.AddSearchDirectory(TargetDir);

        // ReaderParameters { ReadWrite = true } is necessary to later write the file.
        // https://stackoverflow.com/questions/41840455/locked-target-assembly-with-mono-cecil-and-pcl-code-injection
        var readerParameters = new ReaderParameters
        {
            ReadWrite = true,
            AssemblyResolver = resolver
        };

        ProcessAssembly(TargetAssembly, readerParameters, out bool isExecutable);

        foreach (var assemblyPath in Directory.GetFiles(TargetDir, "*.dll"))
        {
            if (assemblyPath == TargetAssembly)
            {
                continue;
            }
            ProcessAssembly(assemblyPath, readerParameters, out _);
        }

        if (_warningMessages != null)
        {
            Log.LogWarning(string.Join(Environment.NewLine, _warningMessages));
        }
        return true;
    }

    private void ProcessAssembly(string assemblyPath, ReaderParameters readerParameters, out bool isExecutable)
    {
        isExecutable = false;
        bool benchmarkMethodsImplAdjusted = false;
        try
        {
            using var module = ModuleDefinition.ReadModule(assemblyPath, readerParameters);
            isExecutable = module.EntryPoint != null;

            foreach (var type in module.Types)
            {
                ProcessType(type, ref benchmarkMethodsImplAdjusted);
            }

            // Write the modified assembly to file.
            module.Write();
        }
        catch (Exception e)
        {
            if (benchmarkMethodsImplAdjusted)
            {
                _warningMessages ??= ["Benchmark methods were found in 1 or more assemblies that require NoInlining, and assembly weaving failed."];
                _warningMessages.Add($"Assembly: {assemblyPath}, error: {e.Message}");
            }
        }
    }

    private void ProcessType(TypeDefinition type, ref bool benchmarkMethodsImplAdjusted)
    {
        // We can skip non-public types as they are not valid for benchmarks.
        if (type.IsNotPublic)
        {
            return;
        }

        // Remove AggressiveInlining and add NoInlining to all [Benchmark] methods.
        foreach (var method in type.Methods)
        {
            if (method.CustomAttributes.Any(IsBenchmarkAttribute))
            {
                var oldImpl = method.ImplAttributes;
                method.ImplAttributes = (oldImpl & ~MethodImplAttributes.AggressiveInlining) | MethodImplAttributes.NoInlining;
                benchmarkMethodsImplAdjusted |= (oldImpl & MethodImplAttributes.NoInlining) == 0;
            }
        }

        // Recursively process nested types
        foreach (var nestedType in type.NestedTypes)
        {
            ProcessType(nestedType, ref benchmarkMethodsImplAdjusted);
        }
    }

    private bool IsBenchmarkAttribute(CustomAttribute attribute)
    {
        // BenchmarkAttribute is unsealed, so we need to walk its hierarchy.
        for (var attr = attribute.AttributeType; attr != null; attr = attr.Resolve()?.BaseType)
        {
            if (attr.FullName == "BenchmarkDotNet.Attributes.BenchmarkAttribute")
            {
                return true;
            }
        }
        return false;
    }
}