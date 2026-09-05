using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("GemmaLauncher.Tests")]

namespace GemmaLauncher.Core;

public sealed class MissingRuntimePrerequisiteException : InvalidOperationException
{
    public Uri DownloadUri { get; } = new("https://aka.ms/vc14/vc_redist.x64.exe");
    public IReadOnlyList<string> MissingLibraries { get; }

    public MissingRuntimePrerequisiteException(IEnumerable<string> missingLibraries)
        : base(Localization.T("engine.prerequisite.vcRuntime"))
    {
        MissingLibraries = Array.AsReadOnly(missingLibraries.ToArray());
    }
}

public static class RuntimePrerequisites
{
    private static readonly string[] RequiredLibraries = ["MSVCP140.dll", "VCRUNTIME140.dll", "VCRUNTIME140_1.dll"];

    public static void EnsureAvailable() => EnsureAvailable(CanLoadSystemLibrary);

    internal static void EnsureAvailable(Func<string, bool> libraryProbe)
    {
        ArgumentNullException.ThrowIfNull(libraryProbe);
        List<string> missing = [];
        foreach (var name in RequiredLibraries)
        {
            var available = false;
            try { available = libraryProbe(Path.Combine(Environment.SystemDirectory, name)); }
            catch (Exception exception) when (exception is BadImageFormatException or DllNotFoundException or
                                              IOException or UnauthorizedAccessException or Win32Exception) { }
            if (!available) missing.Add(name);
        }
        if (missing.Count > 0) throw new MissingRuntimePrerequisiteException(missing);
    }

    private static bool CanLoadSystemLibrary(string path)
    {
        // Avoid the Windows missing-DLL dialog without changing the application's global error mode.
        var previousMode = GetThreadErrorMode();
        if (!SetThreadErrorMode(previousMode | 0x0001u | 0x8000u, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            // Both the requested DLL and its dependencies are restricted to the system directory.
            if (!NativeLibrary.TryLoad(path, typeof(RuntimePrerequisites).Assembly, DllImportSearchPath.System32, out var handle)) return false;
            NativeLibrary.Free(handle);
            return true;
        }
        finally { SetThreadErrorMode(previousMode, out _); }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetThreadErrorMode();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint mode, out uint oldMode);
}
