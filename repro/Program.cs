using System.Diagnostics;
using Microsoft.Diagnostics.NETCore.Client;

// Dumps its own process the way BenchmarkDotNet's in-process disassembler does, and reports whether that returns.
var process = Process.GetCurrentProcess();
Console.WriteLine($"ProcessPath = {Environment.ProcessPath}");
Console.WriteLine($"Runtime     = {Environment.Version} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");

string dumpPath = Path.Combine(Path.GetTempPath(), $"selfdump-{process.Id}.dmp");
var dump = Task.Run(() => new DiagnosticsClient(process.Id).WriteDump(DumpType.Normal, dumpPath, logDumpGeneration: true));

var stopwatch = Stopwatch.StartNew();
if (await Task.WhenAny(dump, Task.Delay(TimeSpan.FromSeconds(60))) != dump)
{
    Console.WriteLine($"RESULT: HUNG (no return after {stopwatch.Elapsed.TotalSeconds:F0}s)");
    return 2;
}

try
{
    await dump;
    Console.WriteLine($"RESULT: OK in {stopwatch.Elapsed.TotalSeconds:F1}s, {new FileInfo(dumpPath).Length / 1024 / 1024} MB");
    File.Delete(dumpPath);
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"RESULT: FAILED {ex.GetType().Name}: {ex.Message}");
    return 1;
}
