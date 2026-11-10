using System.Security.Cryptography;
namespace GemmaLauncher.Core.Accounts;

public sealed class AccountRuntime(AccountStateStore store, IInstallationService installer)
{
    public const string Version = "8.0.27";
    public const string ExecutableSha256 = "A3620A7E8D54D0F4257B493EA5FC01B3C59D581FB8C3596DF5880976596DA162";
    public static RuntimePackage Package => new()
    {
        Version = Version, Url = "https://github.com/router-for-me/CLIProxyAPI/releases/download/v8.0.27/CLIProxyAPI_8.0.27_windows_amd64.zip",
        Bytes = 23629480, Sha256 = "ac6867818696aefe299c8bac1730f831cbb127f0854acca1b1aebb0110a5dc53", ExecutableRelativePath = "cli-proxy-api.exe"
    };
    public async Task<string> EnsureAsync(IProgress<ConnectionProgress>? progress, CancellationToken ct)
    {
        store.RequireOwnership();
        var legacy = Path.Combine(store.DirectoryPath, "runtime", Version, "cli-proxy-api.exe");
        store.CheckPath(legacy);
        progress?.Report(new(AccountPreparationStage.CheckingRuntime));
        if (File.Exists(legacy) && await MatchesAsync(legacy, ct)) return legacy;
        var path = await installer.EnsureRuntimeAsync(Package, new Progress<InstallationProgress>(p => progress?.Report(new(p.Stage == "extracting" ? AccountPreparationStage.ExtractingRuntime : AccountPreparationStage.DownloadingRuntime, p.CompletedBytes, p.TotalBytes))), ct);
        store.CheckPath(path);
        if (!await MatchesAsync(path, ct)) throw new ConnectionFailure("runtime_checksum_mismatch");
        return path;
    }
    internal static async Task<bool> MatchesAsync(string path, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        return input.Length == 72386560 && Convert.ToHexString(await SHA256.HashDataAsync(input, ct)) == ExecutableSha256;
    }
}
