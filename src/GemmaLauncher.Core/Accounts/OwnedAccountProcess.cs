// Adapted from motionsilse/CatRoute c5c74ad, product core. See THIRD-PARTY-NOTICES.md.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GemmaLauncher.Core.Accounts;

internal sealed class OwnedAccountProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly SafeFileHandle job;
    private readonly Task output, error;
    private bool exited;
    public bool HasExited => exited || process.HasExited;

    private OwnedAccountProcess(Process process, SafeFileHandle job)
    { this.process = process; this.job = job; output = DrainAsync(process.StandardOutput); error = DrainAsync(process.StandardError); }

    public static OwnedAccountProcess Start(string executable, string directory, string[] arguments)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); throw new ConnectionFailure("process_ownership_failed"); }
        var limits = new ExtendedLimit { Basic = new BasicLimit { Flags = 0x2000 } }; // Kill children when parent closes this handle.
        if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimit>()))
        { job.Dispose(); throw new ConnectionFailure("process_ownership_failed"); }
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var name in info.Environment.Keys.ToArray())
            if (name.StartsWith("PGSTORE", StringComparison.OrdinalIgnoreCase) || name.StartsWith("GITSTORE", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("OBJECTSTORE", StringComparison.OrdinalIgnoreCase) || new[] { "HOME_JWT", "DEPLOY", "MANAGEMENT_PASSWORD", "GITHUB_TOKEN", "WRITABLE_PATH" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                info.Environment.Remove(name);
        Process? process = null;
        try
        {
            process = Process.Start(info) ?? throw new ConnectionFailure("runtime_start_failed");
            if (!AssignProcessToJobObject(job, process.Handle)) throw new ConnectionFailure("process_ownership_failed");
            return new OwnedAccountProcess(process, job);
        }
        catch
        {
            if (process is not null) { if (!process.HasExited) process.Kill(entireProcessTree: true); process.Dispose(); }
            job.Dispose(); throw;
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        try { while (await reader.ReadAsync(buffer) != 0) { } }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        job.Dispose();
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(budget.Token);
            exited = true;
            try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { process.StandardOutput.Dispose(); process.StandardError.Dispose(); }
        }
        finally { if (exited) process.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct BasicLimit
    { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit
    { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimit info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
