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
    private bool disposed, revealed, loggingIn;
    private int generation;
    private string? error, usageError, diagnosticResult, authenticationError;
    private QueryKind? activeQuery;
    private enum QueryKind { Usage, Models, Request }
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
    public string AccountText
    {
        get
        {
            if (Account.State != SavedAccountState.Available) return T("ui.account.noAccount");
            var identity = string.Join(" · ", new[] { Account.Email, Account.Plan }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return identity.Length > 0 ? identity : T("ui.account.savedIdentity");
        }
    }
    public bool IsLoggingIn => IsBusy && loggingIn && !IsStopping;
    public bool HasUsage => usage is not null;
    private string? LifecycleFailure => error ?? connection.FailureCode;
    public bool NeedsAuthentication => authenticationError is not null;
    public string ServerStatus => T(IsStopping ? "ui.account.stopping" : IsBusy && !loggingIn ? "ui.account.serverStarting" : LifecycleFailure is "stop_failed" or "runtime_exited" ? "ui.account.attention" : IsConnected ? "ui.account.serverRunning" : "ui.account.serverOff");
    public string LoginLabel => T(IsLoggingIn ? stage == AccountPreparationStage.DownloadingRuntime ? "ui.account.cancelDownload" : "ui.account.cancelLogin" : "ui.account.login");
    public string ServerLabel => T(IsStopping ? "ui.account.stopping" : IsBusy && !loggingIn ? "ui.account.cancelStart" : IsConnected || IsRunning ? "ui.account.serverStop" : "ui.account.serverStart");
    public string AccountHint => authenticationError is not null ? FailureText(authenticationError) : IsLoggingIn || Account.State != SavedAccountState.Available ? StatusDescription : T("ui.account.keepLogin");
    public string ServerHint => LifecycleFailure is { } failure && !loggingIn ? FailureText(failure) : IsStopping || IsBusy && !loggingIn ? StatusDescription : T(IsConnected ? "ui.account.runningHint" : "ui.account.offHint");
    public string StatusTitle => T(IsStopping ? "ui.account.stopping" : IsBusy ? "ui.account.preparing" : LifecycleFailure is not null ? "ui.account.attention" : IsConnected ? "ui.account.ready" : Account.State == SavedAccountState.Available ? "ui.account.saved" : Account.State == SavedAccountState.Missing ? "ui.account.loginNeeded" : "ui.account.attention");
    public string PrimaryLabel => IsStopping ? T("ui.account.stopping") : IsBusy ? IsLoggingIn ? LoginLabel : T("ui.account.cancelStart") : IsRunning ? T("ui.account.serverStop") : T(Account.State == SavedAccountState.Available ? "ui.account.serverStart" : "ui.account.login");
    public string StatusDescription => LifecycleFailure is { } failure ? FailureText(failure) : IsStopping ? T("ui.account.stopping") : IsBusy ? StageText() + (total > 0 ? $"  {completed * 100d / total:0}%" : "") : IsConnected ? T("ui.account.readyHint") : Account.State is SavedAccountState.Unreadable or SavedAccountState.Multiple ? T("ui.account.recovery") : T("ui.account.firstUse");
    private string StageText() => stage switch { AccountPreparationStage.CheckingRuntime => T("engine.install.verifying", "CLIProxyAPI"), AccountPreparationStage.DownloadingRuntime => T("engine.install.downloading", "CLIProxyAPI"), AccountPreparationStage.ExtractingRuntime => T("engine.install.extracting"), _ => T("ui.account.stage." + stage) };
    public string UsageText
    {
        get
        {
            if (activeQuery == QueryKind.Usage) return T("ui.account.usageLoading");
            if (usageError is not null) return FailureText(usageError);
            if (usage is null) return T(IsConnected ? "ui.account.usageHint" : "ui.account.usageOff");
            var windows = usage.Windows.Select(window => string.Join("\n",
                window.Bucket + " · " + UsageDuration(window.DurationSeconds),
                window.UsedPercent is double percent and >= 0 and <= 100 ? T("ui.account.remaining", (100-percent).ToString("0.#", CultureInfo.CurrentCulture)) : T("ui.account.remainingUnknown"),
                window.ResetsAt is { } reset ? T("ui.account.resets", reset.ToLocalTime().ToString("g")) : T("ui.account.resetUnknown")));
            return (usage.Windows.Count == 0 ? T("ui.account.usageEmpty") : string.Join("\n\n", windows))
                + "\n\n" + T(IsConnected ? "ui.account.checked" : "ui.account.lastChecked", usage.CheckedAt.ToLocalTime().ToString("g"));
        }
    }
    public string DiagnosticsText => !IsConnected ? LifecycleFailure is { } failure ? FailureText(failure) : T("ui.account.diagnosticsOff")
        : activeQuery is QueryKind.Models or QueryKind.Request ? T(activeQuery == QueryKind.Models ? "ui.account.checkingModels" : "ui.account.testingRequest")
        : diagnosticResult is not null ? FailureText(diagnosticResult) : T("ui.account.diagnosticsHint");
    private static string UsageDuration(long? seconds) => seconds switch
    {
        > 0 when seconds % 86400 == 0 => T("ui.account.days", seconds / 86400),
        > 0 when seconds % 3600 == 0 => T("ui.account.hours", seconds / 3600),
        > 0 when seconds % 60 == 0 => T("ui.account.minutes", seconds / 60),
        > 0 => T("ui.account.seconds", seconds),
        _ => T("ui.account.periodUnknown")
    };
    public bool CanStart => !disposed && !IsStopping && error != "local_key_unreadable" && (IsBusy || IsRunning || Account.State is SavedAccountState.Missing or SavedAccountState.Available);
    public bool CanQuery => IsConnected && !IsBusy && !IsStopping && !IsQueryBusy && !disposed;
    public bool CanReopen => IsBusy && loginUri is not null;
    public bool NeedsRecovery => LifecycleFailure is not null || Account.State is SavedAccountState.Unreadable or SavedAccountState.Multiple;
    public bool NeedsStorageRecovery => error == "local_key_unreadable" || Account.State is SavedAccountState.Unreadable or SavedAccountState.Multiple;
    public Action<Uri> OpenBrowser { get; set; } = uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    public Func<bool> ConfirmSignOut { get; set; } = () => false;
    public RelayCommand PrimaryCommand { get; }
    public RelayCommand LoginCommand { get; }
    public RelayCommand ServerCommand { get; }
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
        PrimaryCommand = Command(() => { if (IsBusy) CancelPending(); else if (IsRunning) _ = StopAsync(); else if (Account.State == SavedAccountState.Missing) _ = LoginAsync(); else _ = StartAsync(); }, () => CanStart);
        LoginCommand = Command(() => { if (IsLoggingIn) CancelPending(); else _ = LoginAsync(); }, () => CanStart && (IsLoggingIn || !IsBusy && Account.State == SavedAccountState.Missing));
        ServerCommand = Command(() => { if (IsBusy) CancelPending(); else if (IsRunning) _ = StopAsync(); else _ = StartAsync(); }, () => CanStart && !IsLoggingIn && Account.State == SavedAccountState.Available);
        UsageCommand = Command(() => _ = QueryUsageAsync(), () => CanQuery);
        RefreshCommand = Command(() => _ = QueryAsync(async ct => { var models = await connection.ModelsAsync(ct); if (!models.Contains(ApiModelId)) throw new ConnectionFailure("default_model_unavailable"); }, QueryKind.Models), () => CanQuery);
        TestCommand = Command(() => _ = QueryAsync(async ct => { var result = await connection.TestAsync(ApiModelId, ct); ct.ThrowIfCancellationRequested(); if (!result.TextMatched) throw new ConnectionFailure("invalid_completion"); }, QueryKind.Request), () => CanQuery);
        SignOutCommand = Command(() => { if (ConfirmSignOut()) _ = SignOutAsync(); }, () => !IsBusy && !IsStopping && !disposed && Account.State == SavedAccountState.Available);
        CancelQueryCommand = Command(() => queryCancellation?.Cancel(), () => IsQueryBusy);
        CopyAllCommand = Command(() => Clipboard.SetText($"API: {ApiAddress}\nAPI key: {connection.LocalApiKey}\nModel: {ApiModelId}"), () => IsConnected);
        KeyCommand = Command(() => { revealed = !revealed; Notify(); }, () => IsConnected);
        ReopenCommand = Command(() => { if (loginUri is not null) { try { OpenBrowser(loginUri); } catch { error = "browser_failed"; Notify(); } } }, () => CanReopen);
        connection.StatusChanged += Changed; Localization.Current.LanguageChanged += LanguageChanged;
    }
    private RelayCommand Command(Action action, Func<bool> can) { var cmd = new RelayCommand(_ => action(), _ => can()); commands.Add(cmd); return cmd; }
    private void Changed(object? sender, EventArgs e) => context.Post(_ => { if (!disposed) { if (connection.FailureCode is not null) { revealed = false; queryCancellation?.Cancel(); } Notify(); } }, null);
    private void LanguageChanged(object? sender, EventArgs e) => Notify();
    private void Notify() { if (disposed) return; OnPropertyChanged(null); foreach (var cmd in commands) cmd.Refresh(); }
    public Task ActivateAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (!IsBusy) Account = connection.ReadSavedAccount(); Notify(); return Task.CompletedTask; }
    public void RecheckSavedState() { if (IsBusy) return; error = null; Account = connection.ReadSavedAccount(); Notify(); }
    public void MaskKey() { revealed = false; Notify(); }
    public void Copy(string value) { if (IsConnected) Clipboard.SetText(value); }
    public string? CopyableKey => IsConnected ? connection.LocalApiKey : null;
    public Task LoginAsync() => IsStopping || Account.State != SavedAccountState.Missing ? Task.CompletedTask : PrepareAsync(true);
    public Task StartAsync() => IsStopping || Account.State != SavedAccountState.Available ? Task.CompletedTask : PrepareAsync(false);
    private Task PrepareAsync(bool login) => RunLifecycleAsync(async ct =>
    {
        loggingIn = login;
        Notify();
        revealed = false; usage = null; usageError = null; diagnosticResult = null;
        var current = generation;
        var progress = new Progress<ConnectionProgress>(p => { if (!disposed && current == generation && !ct.IsCancellationRequested) { stage = p.Stage; completed = p.CompletedBytes; total = p.TotalBytes; Notify(); } });
        if (login) { await connection.LoginAsync(uri => { if (current != generation || ct.IsCancellationRequested) return; loginUri = uri; Notify(); try { OpenBrowser(uri); } catch { throw new ConnectionFailure("browser_failed"); } }, Localization.Current.LanguageCode, progress, ct); authenticationError = null; }
        else await connection.ConnectAsync(progress, ct);
    });
    private Task RunLifecycleAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy || disposed) return Task.CompletedTask;
        IsBusy = true; error = null; stage = IsStopping ? AccountPreparationStage.Stopping : AccountPreparationStage.CheckingRuntime; generation++;
        lifecycle?.Dispose(); lifecycle = new();
        Notify(); return operation = WorkAsync();
        async Task WorkAsync()
        {
            try { await action(lifecycle.Token); }
            catch (OperationCanceledException) when (lifecycle.IsCancellationRequested) { }
            catch (OperationCanceledException) { error = "connection_timeout"; }
            catch (Exception ex) { error = Classify(ex); }
            finally { generation++; loginUri = null; loggingIn = false; IsBusy = false; Account = connection.ReadSavedAccount(); Notify(); }
        }
    }
    public Task QueryUsageAsync() => QueryAsync(async ct => { var result = await connection.UsageAsync(ct); ct.ThrowIfCancellationRequested(); usage = result; }, QueryKind.Usage);
    private Task QueryAsync(Func<CancellationToken, Task> action, QueryKind kind)
    {
        if (!CanQuery) return Task.CompletedTask;
        IsQueryBusy = true; activeQuery = kind;
        if (kind == QueryKind.Usage) usageError = null; else diagnosticResult = null;
        queryCancellation?.Dispose(); queryCancellation = new(); Notify();
        return query = WorkAsync();
        async Task WorkAsync()
        {
            try
            {
                await action(queryCancellation.Token); queryCancellation.Token.ThrowIfCancellationRequested();
                if (kind != QueryKind.Models) authenticationError = null;
                if (kind != QueryKind.Usage) diagnosticResult = kind == QueryKind.Models ? "models_ok" : "test_ok";
            }
            catch (OperationCanceledException) when (queryCancellation.IsCancellationRequested) { if (kind != QueryKind.Usage) diagnosticResult = "diagnostics_cancelled"; }
            catch (OperationCanceledException) { SetFailure(kind switch { QueryKind.Usage => "usage_timeout", QueryKind.Models => "models_timeout", _ => "test_timeout" }); }
            catch (Exception ex) { SetFailure(Classify(ex)); }
            finally { IsQueryBusy = false; activeQuery = null; Notify(); }
            void SetFailure(string code) { if (kind == QueryKind.Usage) usageError = code; else diagnosticResult = code; }
        }
    }
    public void CancelPending() { generation++; lifecycle?.Cancel(); queryCancellation?.Cancel(); }
    private async Task SignOutAsync()
    {
        if (IsStopping || IsBusy || disposed) return;
        IsStopping = true; revealed = false; Notify();
        try
        {
            queryCancellation?.Cancel(); if (query is not null) await query;
            usage = null; usageError = null; diagnosticResult = null;
            await RunLifecycleAsync(ct => connection.SignOutAsync(ct));
            if (Account.State == SavedAccountState.Missing) authenticationError = null;
        }
        finally { IsStopping = false; Notify(); }
    }
    private string Classify(Exception ex)
    {
        var code = ex is ConnectionFailure failure ? failure.HttpStatus switch
        {
            401 => "authentication_required", 403 => "access_denied", _ => failure.Code
        } : "connection_failed";
        if (code is "authentication_required" or "access_denied") authenticationError = code;
        return code;
    }
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
    private string FailureText(string code) => code switch
    {
        "port_in_use" => T("ui.account.portBusy", ApiAddress),
        "default_model_unavailable" => T("ui.account.modelMissing", ApiModelId),
        "browser_failed" => T("ui.note.browserFailed", "") + " " + T("ui.account.login"),
        "login_declined" => T("ui.account.loginDeclined"),
        "stop_failed" => T("ui.account.stopFailed"),
        "authentication_required" => T("ui.account.authRequired"),
        "access_denied" => T("ui.account.accessDenied"),
        "runtime_exited" => T("ui.account.unexpectedExit"),
        _ => T(code switch
        {
            "session_in_use" => "ui.account.locked", "local_key_unreadable" or "account_unreadable" or "multiple_accounts" => "ui.account.recovery",
            "connection_timeout" => "ui.account.connectionTimeout", "usage_timeout" => "ui.account.usageTimeout",
            "models_timeout" => "ui.account.modelsTimeout", "test_timeout" => "ui.account.testTimeout",
            "test_ok" => "ui.account.testOk", "models_ok" => "ui.account.modelsOk", "diagnostics_cancelled" => "ui.account.diagnosticsCancelled", _ => "ui.account.failed"
        })
    };
}
