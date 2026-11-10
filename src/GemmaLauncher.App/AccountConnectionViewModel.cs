using System.Diagnostics;
using System.Globalization;
using GemmaLauncher.Core;
using GemmaLauncher.Core.Accounts;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;
public sealed class AccountConnectionViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAccountConnection connection;
    private readonly SynchronizationContext context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
    private CancellationTokenSource? lifecycle, queryCancellation;
    private Task? operation, query;
    private bool disposed, revealed;
    private int generation;
    private string? error, queryError;
    private AccountUsage? usage;
    private Uri? loginUri;
    private AccountPreparationStage stage;
    private long completed;
    private long? total;
    public AccountSummary Account { get; private set; } = new(SavedAccountState.Missing);
    public bool IsBusy { get; private set; }
    public bool IsQueryBusy { get; private set; }
    public bool IsStopping { get; private set; }
    public bool IsConnected => connection.IsReady;
    public bool IsRunning => connection.IsRunning;
    public string ApiAddress => connection.ApiAddress;
    public string ApiModelId => AccountConnection.DefaultModel;
    public string ApiKeyText => revealed && IsConnected ? connection.LocalApiKey ?? "" : "••••••••••••••••";
    public string KeyLabel => T(revealed ? "ui.account.hideKey" : "ui.account.showKey");
    public string AccountText => Account.State == SavedAccountState.Available ? string.Join(" · ", new[] { Account.Email, Account.Plan }.Where(s => !string.IsNullOrWhiteSpace(s))) : T("ui.account.noAccount");
    public string StatusTitle => T(IsBusy ? "ui.account.preparing" : IsConnected ? "ui.account.ready" : Account.State == SavedAccountState.Available ? "ui.account.saved" : Account.State == SavedAccountState.Missing ? "ui.account.loginNeeded" : "ui.account.attention");
    public string PrimaryLabel => T(IsStopping ? "ui.account.stopping" : IsBusy ? "ui.account.cancel" : IsConnected ? "ui.account.stop" : Account.State == SavedAccountState.Available ? "ui.account.start" : "ui.account.login");
    public string StatusDescription => error is not null ? FailureText(error) : IsBusy ? StageText() + (total > 0 ? $"  {completed * 100d / total:0}%" : "") : IsConnected ? T("ui.account.readyHint") : Account.State is SavedAccountState.Unreadable or SavedAccountState.Multiple ? T("ui.account.recovery") : T("ui.account.firstUse");
    private string StageText() => stage switch { AccountPreparationStage.CheckingRuntime => T("engine.install.verifying", "CLIProxyAPI"), AccountPreparationStage.DownloadingRuntime => T("engine.install.downloading", "CLIProxyAPI"), AccountPreparationStage.ExtractingRuntime => T("engine.install.extracting"), _ => T("ui.account.stage." + stage) };
    public string UsageText => queryError is not null ? FailureText(queryError) : usage is null ? T("ui.account.usageHint") : usage.CheckedAt.ToLocalTime().ToString("g") + "\n" + string.Join("\n", usage.Windows.Select(w => $"{w.Bucket} · {(w.UsedPercent is double p ? p.ToString("0.#", CultureInfo.CurrentCulture) + "%" : "—")} · {(w.ResetsAt?.ToLocalTime().ToString("g") ?? "—")}"));
    public bool CanStart => !disposed && !IsStopping && error is not ("local_key_unreadable" or "stop_failed") && (IsBusy || IsConnected || Account.State is SavedAccountState.Missing or SavedAccountState.Available);
    public bool CanQuery => IsConnected && !IsBusy && !IsQueryBusy && !disposed;
    public bool CanReopen => IsBusy && loginUri is not null;
    public bool NeedsRecovery => error is not null || Account.State is SavedAccountState.Unreadable or SavedAccountState.Multiple;
    public Action<Uri> OpenBrowser { get; set; } = uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    public Func<bool> ConfirmSignOut { get; set; } = () => false;
    public RelayCommand PrimaryCommand { get; }
    public RelayCommand UsageCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand SignOutCommand { get; }
    public RelayCommand CancelQueryCommand { get; }
    public RelayCommand CopyAllCommand { get; }
    public RelayCommand KeyCommand { get; }
    public RelayCommand ReopenCommand { get; }
    private readonly List<RelayCommand> commands = [];
    public AccountConnectionViewModel(IAccountConnection connection)
    {
        this.connection = connection;
        PrimaryCommand = Command(() => { if (IsBusy) CancelPending(); else if (IsConnected) _ = StopAsync(); else _ = StartAsync(); }, () => CanStart);
        UsageCommand = Command(() => _ = QueryUsageAsync(), () => CanQuery);
        RefreshCommand = Command(() => _ = QueryAsync(async ct => { var models = await connection.ModelsAsync(ct); if (!models.Contains(ApiModelId)) throw new ConnectionFailure("default_model_unavailable"); }), () => CanQuery);
        TestCommand = Command(() => _ = QueryAsync(async ct => { var result = await connection.TestAsync(ApiModelId, ct); ct.ThrowIfCancellationRequested(); if (!result.TextMatched) throw new ConnectionFailure("invalid_completion"); queryError = "test_ok"; }), () => CanQuery);
        SignOutCommand = Command(() => { if (ConfirmSignOut()) _ = SignOutAsync(); }, () => !IsBusy && !IsQueryBusy && !disposed && Account.State == SavedAccountState.Available);
        CancelQueryCommand = Command(() => queryCancellation?.Cancel(), () => IsQueryBusy);
        CopyAllCommand = Command(() => Clipboard.SetText($"API: {ApiAddress}\nAPI key: {connection.LocalApiKey}\nModel: {ApiModelId}"), () => IsConnected);
        KeyCommand = Command(() => { revealed = !revealed; Notify(); }, () => IsConnected);
        ReopenCommand = Command(() => { if (loginUri is not null) { try { OpenBrowser(loginUri); } catch { error = "browser_failed"; Notify(); } } }, () => CanReopen);
        connection.StatusChanged += Changed; Localization.Current.LanguageChanged += LanguageChanged;
    }
    private RelayCommand Command(Action action, Func<bool> can) { var cmd = new RelayCommand(_ => action(), _ => can()); commands.Add(cmd); return cmd; }
    private void Changed(object? sender, EventArgs e) => context.Post(_ => { if (!disposed) Notify(); }, null);
    private void LanguageChanged(object? sender, EventArgs e) => Notify();
    private void Notify() { if (disposed) return; OnPropertyChanged(null); foreach (var cmd in commands) cmd.Refresh(); }
    public Task ActivateAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (!IsBusy) Account = connection.ReadSavedAccount(); Notify(); return Task.CompletedTask; }
    public void RecheckSavedState() { if (IsBusy) return; error = null; Account = connection.ReadSavedAccount(); Notify(); }
    public void MaskKey() { revealed = false; Notify(); }
    public void Copy(string value) { if (IsConnected) Clipboard.SetText(value); }
    public string? CopyableKey => IsConnected ? connection.LocalApiKey : null;
    public Task StartAsync() => RunLifecycleAsync(async ct =>
    {
        revealed = false; usage = null; queryError = null;
        var current = generation;
        var progress = new Progress<ConnectionProgress>(p => { if (!disposed && current == generation && !ct.IsCancellationRequested) { stage = p.Stage; completed = p.CompletedBytes; total = p.TotalBytes; Notify(); } });
        if (Account.State == SavedAccountState.Missing) await connection.LoginAsync(uri => { loginUri = uri; Notify(); OpenBrowser(uri); }, Localization.Current.LanguageCode, progress, ct);
        else await connection.ConnectAsync(progress, ct);
    });
    private Task RunLifecycleAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy || disposed) return Task.CompletedTask;
        IsBusy = true; error = null; stage = AccountPreparationStage.CheckingRuntime; generation++;
        lifecycle?.Dispose(); lifecycle = new();
        Notify(); return operation = WorkAsync();
        async Task WorkAsync()
        {
            try { await action(lifecycle.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error = ex is ConnectionFailure f ? f.Code : "connection_failed"; }
            finally { generation++; loginUri = null; IsBusy = false; Account = connection.ReadSavedAccount(); Notify(); }
        }
    }
    public Task QueryUsageAsync() => QueryAsync(async ct => { var result = await connection.UsageAsync(ct); ct.ThrowIfCancellationRequested(); usage = result; });
    private Task QueryAsync(Func<CancellationToken, Task> action)
    {
        if (!CanQuery) return Task.CompletedTask;
        IsQueryBusy = true; queryError = null; queryCancellation?.Dispose(); queryCancellation = new(); Notify();
        return query = WorkAsync();
        async Task WorkAsync()
        {
            try { await action(queryCancellation.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { queryError = ex is ConnectionFailure f ? f.Code : "connection_failed"; }
            finally { IsQueryBusy = false; Notify(); }
        }
    }
    public void CancelPending() { generation++; lifecycle?.Cancel(); queryCancellation?.Cancel(); }
    private async Task SignOutAsync() { queryCancellation?.Cancel(); if (query is not null) await query; revealed = false; usage = null; queryError = null; await RunLifecycleAsync(ct => connection.SignOutAsync(ct)); }
    public async Task StopAsync()
    {
        if (IsStopping) return;
        revealed = false;
        IsStopping = true; Notify();
        try { CancelPending(); if (operation is not null) await operation; if (query is not null) await query; await RunLifecycleAsync(_ => connection.StopAsync(default)); }
        finally { IsStopping = false; Notify(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        try { await StopAsync(); await connection.DisposeAsync(); }
        finally { disposed = true; connection.StatusChanged -= Changed; Localization.Current.LanguageChanged -= LanguageChanged; lifecycle?.Dispose(); queryCancellation?.Dispose(); }
    }
    private static string FailureText(string code) => T(code switch
    {
        "session_in_use" => "ui.account.locked", "local_key_unreadable" or "account_unreadable" or "multiple_accounts" => "ui.account.recovery",
        "port_in_use" => "ui.account.portBusy", "default_model_unavailable" => "ui.account.modelMissing", "test_ok" => "ui.account.testOk", _ => "ui.account.failed"
    });
}
