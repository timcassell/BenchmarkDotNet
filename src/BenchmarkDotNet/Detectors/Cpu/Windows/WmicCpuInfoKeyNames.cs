﻿namespace BenchmarkDotNet.Detectors.Cpu.Windows;

internal static class WmicCpuInfoKeyNames
{
    internal const string NumberOfLogicalProcessors = "NumberOfLogicalProcessors";
    internal const string NumberOfCores = "NumberOfCores";
    internal const string Name = "Name";
    internal const string MaxClockSpeed = "MaxClockSpeed";
}