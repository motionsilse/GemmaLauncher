using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GemmaLauncher.Core;

public sealed record ProcessMemorySnapshot(long WorkingSetBytes, long? PrivateWorkingSetBytes, long? DedicatedGpuBytes, long? SharedGpuBytes);

/// <summary>Resident RAM and WDDM GPU memory for one process. Unavailable counters remain null.</summary>
public sealed class ProcessMetrics : IDisposable
{
    private readonly SafeProcessHandle process;
    private readonly int pid;
    private readonly object gate = new();
    private IntPtr query;
    private IntPtr dedicatedCounter;
    private IntPtr sharedCounter;
    private bool disposed;
    private const uint MoreData = 0x800007D2;
    private const uint FormatLarge = 0x400;

    public ProcessMetrics(int pid)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
        this.pid = pid;
        process = OpenProcess(0x00100410, false, (uint)pid); // SYNCHRONIZE, QUERY_INFORMATION, VM_READ.
        if (process.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            process.Dispose();
            throw new Win32Exception(error);
        }
        if (PdhOpenQueryW(null, 0, out query) == 0)
        {
            if (PdhAddEnglishCounterW(query, @"\GPU Process Memory(*)\Dedicated Usage", 0, out dedicatedCounter) != 0) dedicatedCounter = IntPtr.Zero;
            if (PdhAddEnglishCounterW(query, @"\GPU Process Memory(*)\Shared Usage", 0, out sharedCounter) != 0) sharedCounter = IntPtr.Zero;
        }
        else query = IntPtr.Zero;
    }

    public ProcessMemorySnapshot Read()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var wait = WaitForSingleObject(process, 0);
            if (wait == 0) throw new InvalidOperationException(Localization.T("engine.metrics.processExited"));
            if (wait != 0x102) throw new Win32Exception(Marshal.GetLastWin32Error());
            var memory = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>(), PrivateWorkingSetSize = nuint.MaxValue };
            var extended = K32GetProcessMemoryInfo(process, ref memory, memory.Size);
            long? privateWorkingSet = extended && memory.PrivateWorkingSetSize != nuint.MaxValue ? checked((long)memory.PrivateWorkingSetSize) : null;
            if (!extended)
            {
                // Earlier Windows versions support only the EX prefix; commit is never substituted for resident private RAM.
                memory.Size = (uint)Marshal.OffsetOf<ProcessMemoryCounters>(nameof(ProcessMemoryCounters.PrivateWorkingSetSize));
                if (!K32GetProcessMemoryInfo(process, ref memory, memory.Size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            var collected = query != IntPtr.Zero && PdhCollectQueryData(query) == 0;
            return new(checked((long)memory.WorkingSetSize), privateWorkingSet,
                collected ? ReadGpuCounter(dedicatedCounter) : null,
                collected ? ReadGpuCounter(sharedCounter) : null);
        }
    }

    private long? ReadGpuCounter(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        var itemSize = Marshal.SizeOf<CounterItem>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint length = 0;
            uint count = 0;
            var status = PdhGetFormattedCounterArrayW(counter, FormatLarge, ref length, ref count, IntPtr.Zero);
            if (status != MoreData || length == 0 || length > 16 * 1024 * 1024) return null;
            var allocated = length;
            var buffer = Marshal.AllocHGlobal(checked((int)length));
            try
            {
                status = PdhGetFormattedCounterArrayW(counter, FormatLarge, ref length, ref count, buffer);
                if (status == MoreData) continue;
                if (status != 0 || (ulong)count * (uint)itemSize > allocated) return null;
                long total = 0;
                var matched = false;
                var prefix = $"pid_{pid}_";
                for (var index = 0; index < count; index++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, checked(index * itemSize)));
                    var name = Marshal.PtrToStringUni(item.Name);
                    if (name is null || !name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    if (item.Formatted.Status is not (0 or 1) || item.Formatted.Value < 0) return null;
                    matched = true;
                    total = checked(total + item.Formatted.Value);
                }
                return matched ? total : null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (query != IntPtr.Zero) PdhCloseQuery(query);
            query = IntPtr.Zero;
            process.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue { public uint Status; public long Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem { public IntPtr Name; public CounterValue Formatted; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32GetProcessMemoryInfo(SafeProcessHandle process, ref ProcessMemoryCounters counters, uint size);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? source, nuint userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, nuint userData, out IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr query);
}
