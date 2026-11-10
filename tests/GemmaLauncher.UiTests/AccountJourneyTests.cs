using GemmaLauncher.App;
using GemmaLauncher.Core;
using GemmaLauncher.Core.Accounts;

namespace GemmaLauncher.UiTests;

public static partial class Program
{
    private static async Task TestAccountJourneyAsync()
    {
        Localization.Current.SetLanguage("ko");
        var failures = new List<string>();
        foreach (var saved in new[] { false, true })
        {
            await using var account = new JourneyAccount { Saved = saved };
            await using var vm = new AccountConnectionViewModel(account);
            await vm.ActivateAsync(default);
            using var form = new Form { Size = new(720, 560), Font = new("Segoe UI", 11.5f) };
            using var view = new AccountConnectionView(vm); form.Controls.Add(view); form.Show(); await Task.Delay(40);
            var primary = Controls(form).OfType<Button>().Single(button => button.Name == (saved ? "AccountPrimary" : "AccountLogin"));
            if (!InViewport(primary)) failures.Add("Login/start must be visible without scrolling at minimum size.");
            if (saved)
            {
                var signOut = Controls(form).OfType<Button>().Single(button => ReferenceEquals(button.Tag, vm.SignOutCommand));
                if (!InViewport(signOut) || !signOut.Enabled) failures.Add("A saved account needs an immediately visible logout action, outside collapsed diagnostics.");
            }
            else if (Controls(form).OfType<TextBox>().Any(box => box.Visible))
                failures.Add("Before login, show setup instructions instead of unusable API credentials.");
            form.Close();
        }
        Assert(failures.Count == 0, string.Join("\n", failures));
        await TestAccountVisibleJourneyAsync();
    }

    private static bool InViewport(Control control)
    {
        if (!control.Visible || control.Width <= 0 || control.Height <= 0) return false;
        var bounds = control.RectangleToScreen(control.ClientRectangle);
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
            if (!parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds)) return false;
        return true;
    }

    private static async Task TestAccountVisibleJourneyAsync()
    {
        await using var fixture = new Fixture(); fixture.Settings.ActiveFeature = "chatgpt";
        var account = new JourneyAccount();
        await using var shell = new LoaderShellViewModel(fixture.Settings, fixture.Store, () => fixture.ViewModel, () => new(account));
        using var window = new LoaderWindow(shell); window.Show(); await Task.Delay(40);
        var vm = shell.Account!; var browserOpened = 0;
        vm.OpenBrowser = _ => browserOpened++;
        Button Primary() => Controls(window).OfType<Button>().Single(button => button.Name == (vm.Account.State == SavedAccountState.Missing ? "AccountLogin" : "AccountPrimary"));
        Button SignOut() => Controls(window).OfType<Button>().Single(button => ReferenceEquals(button.Tag, vm.SignOutCommand));
        Assert(!vm.IsConnected && account.Starts == 0 && account.Logins == 0, "Opening ChatGPT must not download, log in, or start the API.");
        await Capture("01-first-use");

        Primary().PerformClick(); await UntilAsync(() => vm.CanReopen); await Task.Delay(60);
        await Capture("02-browser-wait-before-assert");
        Assert(browserOpened == 1 && InViewport(Primary()), "Login must open the browser once and keep cancel visible.");
        var reopen = Controls(window).OfType<Button>().Single(button => ReferenceEquals(button.Tag, vm.ReopenCommand));
        Assert(InViewport(reopen), "Reopen login must be visible during browser waiting.");
        reopen.PerformClick(); Assert(browserOpened == 2, "Reopen must reuse the pending login.");
        await Capture("02-browser-wait");
        window.Size = new(720,560); await Capture("02-browser-wait-small");
        foreach(var language in Localization.Current.Languages) {
            Localization.Current.SetLanguage(language.Code); await Task.Delay(35);
            Assert(InViewport(Primary()) && InViewport(reopen), language.Code+": login cancel and reopen must remain visible at minimum size.");
        }
        Localization.Current.SetLanguage("ko"); window.Size = new(1040,840); await Task.Delay(40);
        Primary().PerformClick(); await UntilAsync(() => !vm.IsBusy);
        Assert(!vm.IsConnected && !account.Saved && !vm.CanReopen, "Cancel login must return to a clean signed-out state.");
        Primary().PerformClick(); await UntilAsync(() => vm.CanReopen); account.LoginGate.TrySetResult();
        await UntilAsync(() => vm.Account.State == SavedAccountState.Available && !vm.IsBusy); await Task.Delay(40);
        Assert(!vm.IsConnected && !vm.IsRunning && account.Starts == 0, "Login alone must leave the public API off.");
        await Capture("03-saved-server-off");
        window.Size = new(720,560); await Capture("03-saved-small");
        foreach(var language in Localization.Current.Languages) {
            Localization.Current.SetLanguage(language.Code); await Task.Delay(35);
            if (!InViewport(Primary()) || !InViewport(SignOut())) await Capture("layout-failure-"+language.Code);
            Assert(InViewport(Primary()) && InViewport(SignOut()), language.Code+": logout and server start must remain visible at minimum size.");
        }
        Localization.Current.SetLanguage("ko"); window.Size = new(1040,840); await Task.Delay(40);
        Primary().PerformClick(); await UntilAsync(() => vm.IsConnected && !vm.IsBusy);
        Assert(InViewport(SignOut()) && SignOut().Enabled, "Connected account must expose logout in the first viewport.");
        Assert(!Controls(window).OfType<TextBox>().Any(box => box.Text == account.LocalApiKey), "The API key must begin masked.");
        await Capture("03-connected");
        window.Size = new(720,560); await Capture("03-connected-small");
        foreach(var language in Localization.Current.Languages) {
            Localization.Current.SetLanguage(language.Code); await Task.Delay(35);
            if (!InViewport(Primary()) || !InViewport(SignOut())) await Capture("layout-failure-"+language.Code);
            Assert(InViewport(Primary()) && InViewport(SignOut()), language.Code+": logout and server stop must remain visible at minimum size.");
        }
        Localization.Current.SetLanguage("ko"); window.Size = new(1040,840); await Task.Delay(40);
        vm.KeyCommand.Execute(null); await Task.Delay(40);
        Assert(Controls(window).OfType<TextBox>().Any(box=>box.Text==account.LocalApiKey), "Reveal must show the local API key.");
        await shell.SelectFeatureAsync(LoaderFeature.LocalModels, default);
        await shell.SelectFeatureAsync(LoaderFeature.ChatGpt, default); await Task.Delay(60);
        Assert(!Controls(window).OfType<TextBox>().Any(box=>box.Text==account.LocalApiKey), "Navigation must mask the API key again.");
        account.FailStop = true; await vm.StopAsync();
        Assert(vm.ServerCommand.CanExecute(null) && vm.StatusDescription == Localization.T("ui.account.stopFailed"), "Failed cleanup must explain the failure and allow stopping again.");
        account.FailStop = false;
        var query = vm.QueryUsageAsync(); await UntilAsync(() => vm.IsQueryBusy);
        Assert(Primary().Enabled, "A usage query must not block stopping the server.");
        Primary().PerformClick(); await UntilAsync(() => !vm.IsBusy && !vm.IsConnected && !vm.IsStopping); await query; await Task.Delay(40);
        Assert(account.Saved && InViewport(SignOut()), "Stop must preserve login and keep logout accessible.");
        await Capture("04-stopped-saved");
        var logins = account.Logins; Primary().PerformClick(); await UntilAsync(() => vm.IsConnected && !vm.IsBusy);
        Assert(account.Logins == logins && account.Starts == 2, "Starting a saved account must not initiate another login.");
        await ConfirmLogout(false);
        Assert(account.Saved && vm.IsConnected && account.SignOuts == 0, "Cancelling logout must leave the account and API running.");
        await ConfirmLogout(true); await UntilAsync(() => !vm.IsBusy && !vm.IsStopping); await Task.Delay(40);
        Assert(!account.Saved && !vm.IsConnected && account.SignOuts == 1 && Primary().Text == Localization.T("ui.account.login"), "Confirmed logout must stop the API and return to visible login.");
        await Capture("06-signed-out");
        window.Size = new(720, 560); await Capture("07-first-use-small");
        foreach (var language in Localization.Current.Languages)
        {
            Localization.Current.SetLanguage(language.Code); await Task.Delay(35);
            Assert(InViewport(Primary()), language.Code + ": login action must remain fully visible at minimum size.");
        }
        Localization.Current.SetLanguage("ko");
        window.Close();

        async Task ConfirmLogout(bool accept)
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 30 };
            var handled = false;
            timer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form != window && form.Text == Localization.T("ui.account.signOut"));
                if (dialog is null) return;
                timer.Stop();
                if (accept) Save(dialog, "05-logout-confirmation");
                var choice = Controls(dialog).OfType<Button>().Single(button => button.DialogResult == (accept ? DialogResult.OK : DialogResult.Cancel));
                handled = InViewport(choice); choice.PerformClick();
            };
            timer.Start(); SignOut().PerformClick(); timer.Stop();
            Assert(handled, "Logout must show a visible confirmation with distinct cancel and logout actions.");
            await Task.Delay(40);
        }
        async Task Capture(string name) { await Task.Delay(60); Save(window, name); }
        void Save(Form form, string name)
        {
            var directory = Path.Combine(Root, "artifacts/cat-loader/account-journey"); Directory.CreateDirectory(directory);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, name + ".png"));
        }
    }

    private sealed class JourneyAccount : IAccountConnection
    {
        public bool Saved, FailStop;
        public int Starts, Logins, SignOuts;
        public TaskCompletionSource LoginGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsReady { get; private set; }
        public bool IsRunning => IsReady;
        public string ApiAddress => "http://127.0.0.1:8319/v1";
        public string? LocalApiKey => IsReady ? "journey-fake-local-key" : null;
        public event EventHandler? StatusChanged;
        public AccountSummary ReadSavedAccount() => new(Saved ? SavedAccountState.Available : SavedAccountState.Missing, Saved ? "test@example.invalid" : null, Saved ? "Plus" : null);
        public Task ConnectAsync(IProgress<ConnectionProgress>? progress, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Starts++; IsReady = true; StatusChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public async Task LoginAsync(Action<Uri> browser, string language, IProgress<ConnectionProgress>? progress, CancellationToken ct)
        {
            Logins++; LoginGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            progress?.Report(new(AccountPreparationStage.AwaitingLogin)); browser(new("https://example.invalid/test-login"));
            await LoginGate.Task.WaitAsync(ct); Saved = true; IsReady = false; StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        public Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([AccountConnection.DefaultModel]);
        public async Task<AccountUsage> UsageAsync(CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); return new(DateTimeOffset.Now, []); }
        public Task<ConnectionProbeResult> TestAsync(string model, CancellationToken ct) => Task.FromResult(new ConnectionProbeResult(true, model, 0));
        public Task StopAsync(CancellationToken ct) { if(FailStop) throw new ConnectionFailure("stop_failed"); IsReady = false; StatusChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public Task SignOutAsync(CancellationToken ct) { SignOuts++; Saved = false; return StopAsync(ct); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
