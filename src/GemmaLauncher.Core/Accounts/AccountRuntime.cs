using System.Security.Cryptography;
namespace GemmaLauncher.Core.Accounts;

public sealed class AccountRuntime(AccountStateStore store, IInstallationService installer)
{
    public const string Version = "8.0.66";
    public const string ExecutableSha256 = "C3275BF0BA5CBC071C5CFD7CA9C1595339E8752457C90E61500F446C661F1077";
    public static RuntimePackage Package => new()
    {
        Version = Version, Url = "https://github.com/router-for-me/CLIProxyAPI/releases/download/v8.0.66/CLIProxyAPI_8.0.66_windows_amd64.zip",
        Bytes = 25849907, Sha256 = "4d6b0eb1b267c5b19bb3fa1a67cd5f19581601177561d97588a61d12916d5e03", ExecutableRelativePath = "cli-proxy-api.exe"
    };
    public async Task<string> EnsureAsync(IProgress<ConnectionProgress>? progress, CancellationToken ct)
    {
        store.RequireOwnership();
        var legacy = Path.Combine(store.DirectoryPath, "runtime", Version, "cli-proxy-api.exe");
        store.CheckPath(legacy);
        progress?.Report(new(AccountPreparationStage.CheckingRuntime));
        if (File.Exists(legacy) && await MatchesAsync(legacy, ct)) return legacy;
        var path = await installer.EnsureRuntimeAsync(Package, new RuntimeProgress(progress), ct);
        store.CheckPath(path);
        if (!await MatchesAsync(path, ct)) throw new ConnectionFailure("runtime_checksum_mismatch");
        return path;
    }
    private sealed class RuntimeProgress(IProgress<ConnectionProgress>? progress) : IProgress<InstallationProgress>
    {
        // Forward in order; the UI owns dispatching. Verification/ready must not
        // look like the runtime has started downloading again after extraction.
        public void Report(InstallationProgress value) => progress?.Report(new(value.Stage switch
        {
            "extracting" => AccountPreparationStage.ExtractingRuntime,
            "verifying" or "ready" => AccountPreparationStage.CheckingRuntime,
            _ => AccountPreparationStage.DownloadingRuntime
        }, value.CompletedBytes, value.TotalBytes));
    }
    internal static async Task<bool> MatchesAsync(string path, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        return input.Length == 78193152 && Convert.ToHexString(await SHA256.HashDataAsync(input, ct)) == ExecutableSha256;
    }
}
