using System.ComponentModel;
using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

public static class MetricsTests
{
    public static Task RunAsync()
    {
        using var metrics = new ProcessMetrics(Environment.ProcessId);
        var before = metrics.Read();
        Assert(before.WorkingSetBytes > 0, "Current process must have resident RAM.");
        Assert(before.DedicatedGpuBytes is null or >= 0 && before.SharedGpuBytes is null or >= 0, "Unavailable GPU memory must be null; measured memory must not be negative.");

        var allocation = GC.AllocateUninitializedArray<byte>(32 * 1024 * 1024, true);
        for (var offset = 0; offset < allocation.Length; offset += 4096) allocation[offset] = 0x5A;
        var after = metrics.Read();
        if (after.PrivateWorkingSetBytes is long privateBytes)
        {
            Assert(privateBytes > 0 && privateBytes <= after.WorkingSetBytes, "Resident private RAM must be a subset of the working set.");
            if (before.PrivateWorkingSetBytes is long previousPrivate)
                Assert(privateBytes - previousPrivate >= 16 * 1024 * 1024, "Touched allocation must increase resident private RAM.");
        }
        GC.KeepAlive(allocation);

        metrics.Dispose();
        try { metrics.Read(); throw new Exception("Disposed metrics should throw."); }
        catch (ObjectDisposedException) { }
        try { using var missing = new ProcessMetrics(int.MaxValue); missing.Read(); throw new Exception("A missing process should throw."); }
        catch (Win32Exception) { }
        return Task.CompletedTask;
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
