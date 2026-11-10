namespace GemmaLauncher.Core.Accounts;

public sealed class AccountConnection : IAccountConnection
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatRouteCodexProxy");
    public const string DefaultModel = "gpt-6-luna";
    private readonly AccountStateStore store;
    private readonly int port;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IDisposable? ownership;
    private AccountHost? host;
    private InstallationService? installer;
    private CancellationTokenSource? active;
    private readonly System.Threading.Timer monitor;
    private readonly object statusGate = new();
    private string? failureCode;
    private bool ready, disposed;
    public AccountConnection(string stateDirectory, int port = 8319)
    {
        store = new(stateDirectory); this.port = port;
        monitor = new(_ => CheckHealth(), null, Timeout.Infinite, Timeout.Infinite);
    }
    public string ApiAddress => $"http://127.0.0.1:{port}/v1";
    public string? LocalApiKey => host?.LocalKey;
    public bool IsRunning => host?.IsRunning == true;
    public bool IsReady { get { lock (statusGate) return ready && IsRunning; } }
    public string? FailureCode { get { lock (statusGate) return failureCode; } }
    private void CheckHealth()
    {
        lock (statusGate)
        {
            if (!ready || IsRunning) return;
            ready = false; failureCode = "runtime_exited";
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    public event EventHandler? StatusChanged;
    public AccountSummary ReadSavedAccount() => store.ReadSummary();
    public Task ConnectAsync(IProgress<ConnectionProgress>? progress, CancellationToken ct) => PrepareAsync(null, "auto", progress, ct);
    public Task LoginAsync(Action<Uri> openBrowser, string uiLanguage, IProgress<ConnectionProgress>? progress, CancellationToken ct) => PrepareAsync(openBrowser, uiLanguage, progress, ct);
    private async Task PrepareAsync(Action<Uri>? browser, string language, IProgress<ConnectionProgress>? progress, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(ct);
        try
        {
            await StopHostAsync();
            active?.Dispose(); active = CancellationTokenSource.CreateLinkedTokenSource(ct); var token = active.Token;
            ownership = store.AcquireOwnership();
            var summary = store.ReadSummary();
            if (summary.State is SavedAccountState.Multiple or SavedAccountState.Unreadable) throw new ConnectionFailure(summary.State == SavedAccountState.Multiple ? "multiple_accounts" : "account_unreadable");
            if (browser is null && summary.State != SavedAccountState.Available) throw new ConnectionFailure("login_required");
            if (browser is not null && summary.State != SavedAccountState.Missing) throw new ConnectionFailure("account_already_saved");
            _ = store.ReadOrCreateLocalKey();
            installer ??= new(new LauncherPaths(store.DirectoryPath), discoverExistingModels: false);
            var executable = await new AccountRuntime(store, installer).EnsureAsync(progress, token);
            // Authentication uses a temporary loopback server. The user's API port
            // is opened only by the separate Connect command.
            host = new(store, executable, browser is null ? port : AuthenticationPort());
            progress?.Report(new(AccountPreparationStage.StartingProxy));
            await host.StartAsync(token);
            monitor.Change(2000, 2000);
            if (browser is not null)
            {
                progress?.Report(new(AccountPreparationStage.AwaitingLogin));
                await new AccountLogin(host.Management, store.ReadSummary).LoginAsync(browser, token, language);
                token.ThrowIfCancellationRequested();
                await StopHostAsync();
                return;
            }
            progress?.Report(new(AccountPreparationStage.ReadingModels));
            var models = await new AccountApi(host.Client).ModelsAsync(token);
            token.ThrowIfCancellationRequested();
            if (store.ReadSummary().State != SavedAccountState.Available) throw new ConnectionFailure("login_not_saved");
            if (!models.Contains(DefaultModel, StringComparer.Ordinal)) throw new ConnectionFailure("default_model_unavailable");
            lock (statusGate) { failureCode = null; ready = true; }
            progress?.Report(new(AccountPreparationStage.Ready)); StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error)
        {
            await StopHostAsync();
            if (browser is not null && error is ConnectionFailure { Code: "port_in_use" }) throw new ConnectionFailure("login_failed");
            if (error is ConnectionFailure or OperationCanceledException) throw;
            throw new ConnectionFailure(error is UnauthorizedAccessException ? "state_access_denied" : "connection_failed");
        }
        finally { gate.Release(); }
    }
    private int AuthenticationPort()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var candidate = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            if (candidate != port && candidate != 1455) return candidate;
        }
        throw new ConnectionFailure("port_in_use");
    }
    public Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct) => new AccountApi(RequireHost().Client).ModelsAsync(ct);
    public Task<AccountUsage> UsageAsync(CancellationToken ct) => new AccountUsageClient(RequireHost().Management).ReadAsync(ct);
    public Task<ConnectionProbeResult> TestAsync(string model, CancellationToken ct) => new AccountApi(RequireHost().Client).TestAsync(model, "low", ct);
    private AccountHost RequireHost() => IsReady && host is not null ? host : throw new ConnectionFailure("not_connected");
    private async Task StopHostAsync(bool release = true)
    {
        // Clear readiness before disposing the process so the monitor cannot
        // misclassify an intentional shutdown as an unexpected exit.
        lock (statusGate) { ready = false; failureCode = null; }
        monitor.Change(Timeout.Infinite, Timeout.Infinite);
        try
        {
            if (host is not null)
            {
                var owned = host;
                try { await owned.DisposeAsync(); }
                finally { if (!owned.IsRunning) host = null; }
            }
        }
        finally
        {
            if (host is null && release) { ownership?.Dispose(); ownership = null; }
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        active?.Cancel();
        await gate.WaitAsync();
        try { await StopHostAsync(); active?.Dispose(); active = null; }
        catch { throw new ConnectionFailure("stop_failed"); }
        finally { gate.Release(); }
    }
    public async Task SignOutAsync(CancellationToken ct)
    {
        active?.Cancel(); await gate.WaitAsync(ct);
        try { ownership ??= store.AcquireOwnership(); await StopHostAsync(false); store.ClearLocalLogin(); }
        finally { if (host is null) { ownership?.Dispose(); ownership = null; } gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        try { await StopAsync(default); }
        finally { monitor.Dispose(); installer?.Dispose(); }
    }
}
