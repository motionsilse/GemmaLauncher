using System.Reflection;
using GemmaLauncher.App;
using GemmaLauncher.Core;
using GemmaLauncher.Core.Accounts;

namespace GemmaLauncher.UiTests;

public static partial class Program
{
    private static async Task TestAccountReviewAsync()
    {
        var failures = new List<Exception>();
        foreach (var test in new Func<Task>[] { TestSignOutControlsAsync, TestAccountTimeoutsAsync, TestAccountFailureMessagesAsync, TestAutoSavedSettingsAsync, TestUsageLabelsAsync })
        {
            try { await test(); Console.WriteLine(test.Method.Name + ": PASS"); }
            catch (Exception error) { failures.Add(error); Console.WriteLine(test.Method.Name + ": FAIL: " + error.Message); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static async Task TestSignOutControlsAsync()
    {
        Localization.Current.SetLanguage("ko");
        var account = new ReviewAccount { SignOutGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var vm = new AccountConnectionViewModel(account);
        await vm.ActivateAsync(default); await vm.StartAsync();
        vm.ConfirmSignOut = () => true;
        vm.SignOutCommand.Execute(null);
        try
        {
            Assert(vm.IsStopping && vm.IsBusy, "Signing out must enter the cleanup state");
            Assert(!vm.PrimaryCommand.CanExecute(null) && !vm.SignOutCommand.CanExecute(null), "Cleanup cannot offer cancellation or another sign-out");
            Assert(vm.PrimaryLabel == Localization.T("ui.account.stopping") && vm.StatusTitle == vm.PrimaryLabel, "Sign-out must say stopping, not preparing");
            vm.PrimaryCommand.Execute(null);
            Assert(!account.SignOutToken.IsCancellationRequested, "Disabled primary action must not cancel sign-out");
        }
        finally { account.SignOutGate.TrySetResult(); await UntilAsync(() => !vm.IsBusy); }
        Assert(!vm.IsStopping && vm.Account.State == SavedAccountState.Missing && vm.PrimaryCommand.CanExecute(null), "Completed sign-out returns to login");
    }

    private static async Task TestAccountTimeoutsAsync()
    {
        var account = new ReviewAccount { Connect = InternalTimeoutAsync };
        await using var vm = new AccountConnectionViewModel(account);
        await vm.ActivateAsync(default); await vm.StartAsync();
        Assert(vm.NeedsRecovery && vm.StatusDescription == Localization.T("ui.account.connectionTimeout"), "An internal connection deadline must show a timeout error");

        account.Connect = ct => Task.Delay(Timeout.Infinite, ct);
        var cancelled = vm.StartAsync(); vm.CancelPending(); await cancelled;
        Assert(!vm.NeedsRecovery, "User cancellation must not become an error");
        account.Connect = _ => Task.CompletedTask; await vm.StartAsync();
        foreach (var (command, key) in new[] { (vm.UsageCommand, "usageTimeout"), (vm.RefreshCommand, "modelsTimeout"), (vm.TestCommand, "testTimeout") })
        {
            account.Query = InternalTimeoutAsync;
            command.Execute(null); await UntilAsync(() => !vm.IsQueryBusy);
            Assert((ReferenceEquals(command, vm.UsageCommand) ? vm.UsageText : vm.DiagnosticsText) == Localization.T("ui.account." + key), "Query deadlines must identify the timed-out operation in its own result: " + key);
            Assert(vm.IsConnected && vm.PrimaryCommand.CanExecute(null), "A query timeout must preserve connection and stop controls");
        }
        account.Query = ct => Task.Delay(Timeout.Infinite, ct);
        var query = vm.QueryUsageAsync(); vm.CancelQueryCommand.Execute(null); await query;
        Assert(vm.UsageText == Localization.T("ui.account.usageHint"), "Explicit query cancellation must not display a timeout");
    }

    private static async Task InternalTimeoutAsync(CancellationToken ct)
    {
        using var http = new HttpClient(new DeadlineHandler()) { BaseAddress = new("http://127.0.0.1/v1/"), Timeout = TimeSpan.FromMilliseconds(30) };
        await new AccountApi(http).ModelsAsync(ct);
    }

    private static async Task TestAccountFailureMessagesAsync()
    {
        var account = new ReviewAccount();
        await using var vm = new AccountConnectionViewModel(account);
        await vm.ActivateAsync(default);
        foreach (var language in Localization.Current.Languages)
        {
            Localization.Current.SetLanguage(language.Code);
            foreach (var (code, detail) in new[] { ("session_in_use", "CatRoute"), ("port_in_use", "8319"), ("login_port_in_use", "1455"), ("default_model_unavailable", "gpt-6-luna") })
            {
                account.Connect = _ => Task.FromException(new ConnectionFailure(code));
                await vm.StartAsync();
                Assert(vm.StatusDescription.Contains(detail) && !vm.StatusDescription.Contains(Localization.T("ui.account.failed")), language.Code + ": failure must explain its specific cause: " + code);
                Assert(!vm.StatusDescription.Contains("{0}"), "Failure must format its connection details");
            }
        }
        Localization.Current.SetLanguage("ko");
    }

    private static async Task TestAutoSavedSettingsAsync()
    {
        await using var fixture = new Fixture();
        fixture.Settings.ActiveFeature = "chatgpt";
        await using var shell = new LoaderShellViewModel(fixture.Settings, fixture.Store, () => fixture.ViewModel, () => new(new ReviewAccount()));
        using var window = new LoaderWindow(shell);
        using var dialog = (Form)typeof(LoaderWindow).GetMethod("SettingsDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        dialog.Show(window); await Task.Delay(20);
        var buttons = Controls(dialog).OfType<Button>().Where(button => button.Visible).ToArray();
        Assert(buttons.Length == 1 && buttons[0].Text == Localization.T("ui.close"), "Instant-save settings must offer Close only, not a misleading Cancel");
        Assert(Controls(dialog).OfType<Label>().Any(label => label.Text == Localization.T("ui.settings.autoSaved")), "Settings must explain automatic saving");
        var closeToTray = Controls(dialog).OfType<CheckBox>().Single(); closeToTray.Checked = !closeToTray.Checked;
        Assert(fixture.Store.Load().CloseToTray == closeToTray.Checked, "Settings must persist immediately");
        var language = Controls(dialog).OfType<ComboBox>().Single();
        language.SelectedItem = language.Items.Cast<LanguageOption>().Single(option => option.Code == "en");
        await Task.Delay(20);
        Assert(buttons[0].Text == Localization.T("ui.close") && fixture.Store.Load().LanguagePreference == "en", "Close and saved settings must follow language changes");
        Assert(ReferenceEquals(dialog.CancelButton, buttons[0]), "Escape must close instant-save settings");
        dialog.Close(); Localization.Current.SetLanguage("ko");
    }

    private static async Task TestUsageLabelsAsync()
    {
        Localization.Current.SetLanguage("en");
        var account = new ReviewAccount
        {
            Usage = new(DateTimeOffset.Now, [new("Codex", 20, 18000, DateTimeOffset.Now.AddHours(2)), new("Codex", 70, 604800, null), new("Extra", null, null, null)])
        };
        await using var vm = new AccountConnectionViewModel(account);
        await vm.ActivateAsync(default); await vm.StartAsync(); await vm.QueryUsageAsync();
        Assert(vm.UsageText.Contains("5 hours") && vm.UsageText.Contains("7 days"), "Usage must distinguish five-hour and seven-day limits");
        Assert(vm.UsageText.Contains("Remaining quota: 80%") && !vm.UsageText.Contains("Used:" ) && vm.UsageText.Contains("Resets:") && vm.UsageText.Contains("Checked:"), "Only remaining quota and clearly labelled dates should be displayed");
        Assert(vm.UsageText.Contains("Period not provided") && vm.UsageText.Contains("Remaining quota unavailable") && vm.UsageText.Contains("Reset time not provided"), "Missing usage fields must remain explicitly unknown");
        foreach (var language in Localization.Current.Languages)
        {
            Localization.Current.SetLanguage(language.Code);
            foreach (var used in new double?[] { 0,20,100,12.5,null,-1,101,double.NaN })
            {
                account.Usage = new(DateTimeOffset.Now,[new("Codex",used,2592000,null)]); await vm.QueryUsageAsync();
                if (used is >= 0 and <= 100)
                {
                    var expected = Localization.T("ui.account.remaining",(100-used.Value).ToString("0.#",System.Globalization.CultureInfo.CurrentCulture));
                    Assert(vm.UsageText.Contains(expected),language.Code+": remaining percentage must complement used percentage: "+used);
                    Assert(!vm.UsageText.Contains(Localization.T("ui.account.used",used.Value.ToString("0.#",System.Globalization.CultureInfo.CurrentCulture))),language.Code+": consumed usage must not be displayed");
                }
                else Assert(vm.UsageText.Contains(Localization.T("ui.account.remainingUnknown")) && !vm.UsageText.Contains("%"),language.Code+": unknown or invalid quota must not be displayed as zero remaining");
            }
        }
        Localization.Current.SetLanguage("en");
        account.Usage = new(DateTimeOffset.Now, []); await vm.QueryUsageAsync();
        Assert(vm.UsageText.Contains("No usage limits were provided"), "An empty quota response must not show only a timestamp");
        Localization.Current.SetLanguage("ko");
    }

    private sealed class DeadlineHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { await Task.Delay(Timeout.Infinite, ct); return new(); }
    }

    private sealed class ReviewAccount : IAccountConnection
    {
        public Func<CancellationToken, Task> Connect = _ => Task.CompletedTask;
        public Func<CancellationToken, Task> Query = _ => Task.CompletedTask;
        public AccountUsage Usage = new(DateTimeOffset.Now, []);
        public TaskCompletionSource? SignOutGate;
        public CancellationToken SignOutToken;
        private bool saved = true;
        public bool IsReady { get; private set; }
        public bool IsRunning => IsReady;
        public string ApiAddress => "http://127.0.0.1:8319/v1";
        public string? LocalApiKey => null;
        public event EventHandler? StatusChanged { add { } remove { } }
        public AccountSummary ReadSavedAccount() => new(saved ? SavedAccountState.Available : SavedAccountState.Missing);
        public async Task ConnectAsync(IProgress<ConnectionProgress>? progress, CancellationToken ct) { await Connect(ct); IsReady = true; }
        public Task LoginAsync(Action<Uri> browser, string language, IProgress<ConnectionProgress>? progress, CancellationToken ct) => ConnectAsync(progress, ct);
        public async Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct) { await Query(ct); return [AccountConnection.DefaultModel]; }
        public async Task<AccountUsage> UsageAsync(CancellationToken ct) { await Query(ct); return Usage; }
        public async Task<ConnectionProbeResult> TestAsync(string model, CancellationToken ct) { await Query(ct); return new(true, model, 0); }
        public async Task SignOutAsync(CancellationToken ct) { SignOutToken = ct; if (SignOutGate is not null) await SignOutGate.Task; IsReady = false; saved = false; }
        public Task StopAsync(CancellationToken ct) { IsReady = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
