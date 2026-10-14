using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

public static class PrerequisiteTests
{
    public static Task RunAsync()
    {
        // This host successfully runs the shipped native engine; its x64 prerequisite probe must agree.
        RuntimePrerequisites.EnsureAvailable();
        List<string> paths = [];
        RuntimePrerequisites.EnsureAvailable(path => { paths.Add(path); return true; });
        if (paths.Count != 3 || paths.Any(path => !Path.IsPathFullyQualified(path) ||
                !string.Equals(Path.GetDirectoryName(path), Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase)))
            throw new Exception("VC++ probing must use three absolute System32 DLL paths.");

        var missing = ExpectMissing(path => !Path.GetFileName(path).Equals("VCRUNTIME140_1.dll", StringComparison.OrdinalIgnoreCase));
        if (missing.MissingLibraries.Count != 1 || missing.MissingLibraries[0] != "VCRUNTIME140_1.dll")
            throw new Exception("Missing prerequisite should identify the failed library.");
        if (missing.DownloadUri.AbsoluteUri != "https://aka.ms/vc14/vc_redist.x64.exe" ||
            !missing.Message.Contains("Visual C++", StringComparison.Ordinal) || !missing.Message.Contains("x64", StringComparison.Ordinal))
            throw new Exception("Missing prerequisite must name VC++ x64 and provide the official installer URI.");

        var corrupt = ExpectMissing(_ => throw new BadImageFormatException("Wrong architecture or damaged DLL."));
        if (corrupt.MissingLibraries.Count != 3) throw new Exception("Corrupt libraries must not pass the prerequisite probe.");
        var absent = ExpectMissing(_ => throw new DllNotFoundException());
        if (absent.MissingLibraries.Count != 3) throw new Exception("Loader failures must produce an actionable prerequisite error.");
        return Task.CompletedTask;
    }

    private static MissingRuntimePrerequisiteException ExpectMissing(Func<string, bool> probe)
    {
        try { RuntimePrerequisites.EnsureAvailable(probe); }
        catch (MissingRuntimePrerequisiteException exception) { return exception; }
        throw new Exception("A missing native prerequisite was accepted.");
    }
}
