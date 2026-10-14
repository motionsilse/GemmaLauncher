using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using GemmaLauncher.App;

namespace GemmaLauncher.UiTests;
public static partial class Program
{
    // Run separately so loading PDH for the observer cannot inflate the timed launcher samples.
    private static async Task GraphicsProbeAsync(string reportPath)
    {
        await using var fixture = new Fixture(); await UntilAsync(() => !fixture.ViewModel.IsBusy);
#if WPF_BASELINE
        var window = new MainWindow { DataContext = fixture.ViewModel, Width = 1080, Height = 860 };
#else
        using var window = new MainWindow(fixture.ViewModel) { Size = new System.Drawing.Size(1080, 860) };
#endif
        try
        {
            window.Show(); await Task.Delay(5000);
            using var metrics = new Core.ProcessMetrics(Environment.ProcessId);
            var sample = await Task.Run(metrics.Read);
            var path = Path.GetFullPath(reportPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { sample.DedicatedGpuBytes, sample.SharedGpuBytes,
                environment = "Private, inactive Windows desktop. Null means no matching WDDM counter; it does not mean zero usage." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Close(); }
    }

    // Shared verbatim with the baseline harness; WPF_BASELINE changes only window construction.
    private static async Task BenchmarkAsync(string reportPath)
    {
        await using var fixture = new Fixture(); var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
#if WPF_BASELINE
        var window = new MainWindow { DataContext = vm, Width = 1080, Height = 860 };
#else
        using var window = new MainWindow(vm) { Size = new System.Drawing.Size(1080, 860) };
#endif
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--metrics-child" }, UseShellExecute = false, CreateNoWindow = true })!;
        fixture.Session.ProcessId = child.Id;
        var samples = new List<object>();
        try
        {
            window.Show(); await Sample("visible-idle");
            window.Hide(); await Sample("hidden-idle");
            window.Show(); vm.PrimaryCommand.Execute(null); await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
            await Sample("visible-simulated-server");
            window.Hide(); await Sample("hidden-simulated-server");
            var path = Path.GetFullPath(reportPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), window = "1080x860", sampleSeconds = 10, warmupSeconds = 5,
                inference = "Separate idle child process, no model loaded. Launcher-only telemetry overhead; not real llama.cpp inference.", samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Close(); if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }

        async Task Sample(string state)
        {
            await Task.Delay(5000);
            using var process = Process.GetCurrentProcess();
            process.Refresh(); var cpu = process.TotalProcessorTime.TotalMilliseconds; GetProcessIoCounters(process.Handle, out var startIo);
            var watch = Stopwatch.StartNew(); var ws = new List<long>(); var privateBytes = new List<long>();
            for (var i = 0; i < 10; i++) { await Task.Delay(1000); process.Refresh(); ws.Add(process.WorkingSet64); privateBytes.Add(process.PrivateMemorySize64); }
            GetProcessIoCounters(process.Handle, out var endIo); process.Refresh(); child.Refresh();
            var modules = process.Modules.Cast<ProcessModule>().Select(m => m.ModuleName).Where(n => n.Contains("wpfgfx", StringComparison.OrdinalIgnoreCase) || n.Contains("Presentation", StringComparison.OrdinalIgnoreCase)).ToArray();
            samples.Add(new { state, elapsedMs = watch.Elapsed.TotalMilliseconds, cpuMs = process.TotalProcessorTime.TotalMilliseconds - cpu,
                workingSetBytes = ws.Average(), privateBytes = privateBytes.Average(),
                ioReadBytes = endIo.ReadBytes - startIo.ReadBytes, ioWriteBytes = endIo.WriteBytes - startIo.WriteBytes,
                ioReadOperations = endIo.ReadOperations - startIo.ReadOperations, ioWriteOperations = endIo.WriteOperations - startIo.WriteOperations,
                gdiObjects = GetGuiResources(process.Handle, 0), userObjects = GetGuiResources(process.Handle, 1), wpfModules = modules,
                childWorkingSetBytes = child.WorkingSet64, childPrivateBytes = child.PrivateMemorySize64 });
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll")]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
