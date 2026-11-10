// Adapted from motionsilse/CatRoute c5c74ad, product core. See THIRD-PARTY-NOTICES.md.
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace GemmaLauncher.Core.Accounts;

public sealed record CodexAccount(string? Email, string Plan);

public sealed class AccountHost : IAsyncDisposable
{
    private readonly string directory, executable, managementKey;
    private readonly int port;
    private readonly AccountStateStore store;
    private OwnedAccountProcess? process;
    public HttpClient Client { get; }
    internal HttpClient Management { get; }
    public Uri BaseUri { get; }
    public string LocalKey { get; }
    public bool IsRunning => process is not null && !process.HasExited;

    public Task<AccountUsage> ReadUsageAsync(CancellationToken ct) => new AccountUsageClient(Management).ReadAsync(ct);

    public AccountHost(AccountStateStore store, string executable, int port = 8319)
    {
        if (port is < 1024 or > 65535) throw new ConnectionFailure("invalid_port");
        this.store = store; store.RequireOwnership(); this.directory = store.DirectoryPath; this.executable = Path.GetFullPath(executable); this.port = port;
        try
        {
            LocalKey = store.ReadOrCreateLocalKey();
            store.CheckPath(store.AuthDirectory);
            managementKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Directory.CreateDirectory(Path.Combine(this.directory, "auth"));
            BaseUri = new Uri($"http://127.0.0.1:{port}/v1/");
            Client = CreateClient(BaseUri, LocalKey);
            Management = CreateClient(new Uri(BaseUri, "/v0/management/"), managementKey);
        }
        catch { throw; }
    }

    private static HttpClient CreateClient(Uri uri, string key)
    {
        var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", key); return http;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;
        await StopAsync();
        if (Accounts().Count > 1) throw new ConnectionFailure("multiple_accounts");
        if (!File.Exists(executable)) throw new ConnectionFailure("runtime_missing");
        using (var file = File.OpenRead(executable))
            if (Convert.ToHexString(await SHA256.HashDataAsync(file, ct)) != AccountRuntime.ExecutableSha256) throw new ConnectionFailure("runtime_checksum_mismatch");
        using (var check = new TcpListener(IPAddress.Loopback, port))
        {
            check.Server.ExclusiveAddressUse = true;
            try { check.Start(); } catch (SocketException) { throw new ConnectionFailure("port_in_use"); }
            finally { check.Stop(); }
        }
        var configPath = Path.Combine(directory, "config.yaml");
        var config = new Dictionary<string, object>
        {
            ["config-version"] = 8,
            ["server"] = new Dictionary<string, object> { ["host"] = "127.0.0.1", ["port"] = port, ["commercial-mode"] = true,
                ["max-request-body-bytes"] = 1_048_576, ["discovery"] = new { enabled = false } },
            ["management"] = new Dictionary<string, object> { ["allow-remote"] = false, ["secret-key"] = managementKey,
                ["disable-control-panel"] = true, ["disable-auto-update-panel"] = true },
            ["access"] = new Dictionary<string, object> { ["api-keys"] = new[] { LocalKey } },
            ["oauth"] = new Dictionary<string, object> { ["auth-dir"] = Path.Combine(directory, "auth").Replace('\\', '/') },
            ["routing"] = new { strategy = "fill-first", retry = new Dictionary<string, object>
                { ["request-retry"] = 0, ["max-retry-credentials"] = 1, ["max-retry-interval"] = 0 } },
            ["quota-exceeded"] = new Dictionary<string, object> { ["switch-project"] = false, ["switch-preview-model"] = false },
            ["observability"] = new { logs = new Dictionary<string, object> { ["debug"] = false, ["logging-to-file"] = false, ["request-log"] = false },
                usage = new Dictionary<string, object> { ["usage-statistics-enabled"] = false }, pprof = new { enable = false } },
            ["multimedia"] = new Dictionary<string, object> { ["disable-image-generation"] = true }
        };
        store.AtomicWrite(configPath, JsonSerializer.SerializeToUtf8Bytes(config));
        try
        {
            process = OwnedAccountProcess.Start(executable, directory, ["-config", configPath, "-local-model"]);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(15));
            while (true)
            {
                budget.Token.ThrowIfCancellationRequested();
                if (!IsRunning) throw new ConnectionFailure("runtime_exited");
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "auth-files");
                    using var ready = await AccountApi.SendAsync(Management, request, budget.Token);
                    if (ready.RootElement.TryGetProperty("files", out _)) return;
                    throw new ConnectionFailure("unexpected_runtime_response");
                }
                catch (HttpRequestException) { await Task.Delay(100, budget.Token); }
            }
        }
        catch { await StopAsync(); throw; }
    }

    public IReadOnlyList<CodexAccount> Accounts()
    {
        var summary = store.ReadSummary();
        return summary.State switch
        {
            SavedAccountState.Missing => [],
            SavedAccountState.Available => [new(summary.Email, summary.Plan ?? "unknown")],
            SavedAccountState.Multiple => throw new ConnectionFailure("multiple_accounts"),
            _ => throw new ConnectionFailure("account_unreadable")
        };
    }

    public async Task StopAsync()
    {
        if (process is not null) { var owned = process; await owned.DisposeAsync(); process = null; }
        store.CheckPath(Path.Combine(directory, "config.yaml"));
        File.Delete(Path.Combine(directory, "config.yaml"));
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); } finally { Client.Dispose(); Management.Dispose(); }
    }
}
