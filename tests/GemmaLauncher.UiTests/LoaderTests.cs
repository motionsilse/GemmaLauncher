using GemmaLauncher.App;
using GemmaLauncher.Core.Accounts;
using GemmaLauncher.Core;
namespace GemmaLauncher.UiTests;
public static partial class Program
{
    private static async Task TestLoaderAsync()
    {
        await TestLoaderSettingsSaveFailureAsync();
        await TestStartupRegistrationAsync();
        await using var fixture = new Fixture();
        var account = new FakeAccount();
        var paths = new GemmaLauncher.Core.LauncherPaths(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var settings = new GemmaLauncher.Core.LauncherSettings { ActiveFeature = "chatgpt" };
        var localCalls = 0;
        await using var shell = new LoaderShellViewModel(settings, new(paths), () => { localCalls++; return fixture.ViewModel; }, () => new(account));
        var available = new TaskCompletionSource<ReleaseUpdate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ReleaseUpdate(new Version(0, 10, 0), new Uri("https://github.com/motionsilse/GemmaLauncher/releases/tag/v0.10.0"),
            new(new Uri("https://github.com/motionsilse/GemmaLauncher/releases/download/v0.10.0/CatLoader-0.10.0-win-x64.exe"), 3, new string('0', 64)));
        Uri? openedRelease = null;
        var attempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installCalls = 0;
        using var window = new LoaderWindow(shell, checkUpdate: token => available.Task.WaitAsync(token), openRelease: uri => openedRelease = uri,
            installUpdate: async (target, progress, token) =>
            {
                Assert(target == release, "The requested package must match the update shown.");
                if (++installCalls != 1) return;
                progress.Report(42);
                await attempt.Task.WaitAsync(token);
                throw new IOException("Simulated download failure");
            });
        window.Show(); await Task.Delay(50);
        var update = Controls(window).OfType<Button>().Single(button => button.Name == "UpdateNotice");
        Assert(!update.Visible, "The update notice must stay hidden until a new release is found.");
        available.SetResult(release);
        await UntilAsync(() => update.Visible);
        Assert(update.Text == Localization.T("ui.update.install", "0.10.0"), "The notice must show the new version and restart action.");
        update.PerformClick();
        await UntilAsync(() => !update.Enabled && update.Text == Localization.T("ui.update.progress", 42));
        Assert(openedRelease is null, "Clicking a verified update should download in the app.");
        attempt.SetResult();
        var failure = Controls(window).OfType<Label>().Single(label => label.Name == "UpdateError");
        await UntilAsync(() => failure.Visible && update.Enabled);
        Controls(window).OfType<Button>().Single(button => button.Name == "UpdateReleasePage").PerformClick();
        Assert(openedRelease == release.DownloadPage, "The fallback must open that version's official download page.");
        update.PerformClick();
        await UntilAsync(() => installCalls == 2 && !failure.Visible && update.Enabled);
        Assert(localCalls == 0 && account.Starts == 0, "Restoring ChatGPT must not create local VM or connect");
        var vm = shell.Account!;
        await vm.StartAsync();
        Assert(vm.IsConnected && account.Starts == 1, "Explicit connect becomes ready");
        var query = vm.QueryUsageAsync(); await Task.Delay(10);
        Assert(vm.IsQueryBusy && vm.PrimaryCommand.CanExecute(null), "Usage query must keep stop enabled");
        await vm.StopAsync(); await query;
        Assert(!vm.IsConnected && !vm.IsQueryBusy, "Stop cancels pending query");
        await vm.StartAsync();
        await shell.SelectFeatureAsync(LoaderFeature.LocalModels, default);
        Assert(localCalls == 1, "Create local only on selection");
        Assert(update.Visible, "The notice must be visible from either app feature.");
        await Task.Delay(100);
        var localAction = Controls(window).OfType<Button>().Single(b => b.Name == "PrimaryAction");
        Assert(localAction.Visible && window.ClientRectangle.Contains(window.RectangleToClient(localAction.RectangleToScreen(localAction.ClientRectangle))), "Local primary must stay visible in unified shell");
        localAction.PerformClick(); await UntilAsync(() => fixture.ViewModel.IsServerRunning && !fixture.ViewModel.IsBusy);
        Assert(vm.IsConnected, "Starting local server must keep account connection");
        window.WindowState = FormWindowState.Minimized;
        await UntilAsync(() => !window.Visible);
        Assert(fixture.ViewModel.IsServerRunning && vm.IsConnected, "Minimizing to the tray must preserve both running connections.");
        window.WindowState = FormWindowState.Normal;
        window.Show();
        Assert(fixture.ViewModel.IsServerRunning && vm.IsConnected, "Restoring the window must preserve both running connections.");
        fixture.ViewModel.SelectedContext = fixture.ViewModel.ContextChoices.Last();
        Assert(fixture.ViewModel.HasPendingContext, "The recovery test must have a load option that has not been applied.");
        fixture.Session.ExitUnexpectedly();
        await UntilAsync(() => fixture.Session.Starts.Count == 2 && fixture.ViewModel.IsServerRunning && !fixture.ViewModel.IsBusy);
        Assert(fixture.Session.Starts[0].Context == fixture.Session.Starts[1].Context, "Recovery must reuse the live configuration without applying pending load options.");
        fixture.Session.ExitUnexpectedly();
        Assert(fixture.Session.Starts.Count == 2 && !fixture.ViewModel.IsServerRunning && fixture.ViewModel.HasError, "A second unexpected exit must surface the failure instead of looping.");
        fixture.ViewModel.PrimaryCommand.Execute(null);
        await UntilAsync(() => fixture.ViewModel.IsServerRunning && !fixture.ViewModel.IsBusy);
        account.StoppingGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = vm.StopAsync();
        try
        {
            Button? backgroundStop = null;
            await UntilAsync(() => (backgroundStop = Controls(window).OfType<Button>().SingleOrDefault(b => b.Visible && b.Text.Contains(Localization.T("ui.loader.chat")) && b.Text.Contains(Localization.T("ui.account.stopping")))) is not null);
            Assert(!backgroundStop!.Enabled, "Background stop must be disabled during cleanup");
        }
        finally { account.StoppingGate.TrySetResult(); }
        await stopping;
        Assert(fixture.ViewModel.IsServerRunning, "Stopping account must keep local server running");
        await vm.StartAsync(); fixture.ViewModel.PrimaryCommand.Execute(null); await UntilAsync(() => !fixture.ViewModel.IsServerRunning && !fixture.ViewModel.IsBusy);
        Assert(vm.IsConnected, "Stopping local server must keep account connection");
        fixture.ViewModel.PageIndex = 2; fixture.ViewModel.PresetPrompt += "\nUncommitted loader test";
        fixture.ViewModel.ResolvePresetEdits = () => PresetEditDecision.Cancel;
        Assert(!await shell.SelectFeatureAsync(LoaderFeature.ChatGpt,default) && fixture.ViewModel.HasPresetChanges, "Keep editing must cancel category navigation");
        fixture.ViewModel.ResolvePresetEdits = () => PresetEditDecision.Discard;
        window.Close();
        if (Directory.Exists(paths.Root)) Directory.Delete(paths.Root, true);
        await TestLoaderCleanupAsync();
    }

    private static async Task TestStartupRegistrationAsync()
    {
        var keyPath = @"Software\CatLoader.Tests-" + Guid.NewGuid().ToString("N");
        var executable = Path.Combine(Path.GetTempPath(), "Cat Loader test.exe");
        var startup = new WindowsStartup(executable, keyPath);
        try
        {
            Assert(!startup.IsEnabled, "Startup is opt-in.");
            startup.SetEnabled(true);
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath))
                Assert(key?.GetValue("Cat Loader") as string == $"\"{executable}\" --start-in-tray", "Windows startup must pass the hidden start argument and quote the executable path.");
            var replacement = new WindowsStartup(Path.Combine(Path.GetTempPath(), "Cat Loader newer.exe"), keyPath);
            replacement.RefreshExecutable();
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath))
                Assert((key?.GetValue("Cat Loader") as string)?.Contains("Cat Loader newer.exe", StringComparison.Ordinal) == true, "An update must refresh the registered executable.");
            startup.SetEnabled(false);
            Assert(!startup.IsEnabled, "Disabling startup must remove the launcher entry.");

            await using var fixture = new Fixture();
            await using var shell = new LoaderShellViewModel(fixture.Settings, fixture.Store, () => fixture.ViewModel, () => new(new FakeAccount()), startup: startup);
            using var window = new LoaderWindow(shell);
            window.Show();
            using var dialog = (Form)typeof(LoaderWindow).GetMethod("SettingsDialog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null)!;
            dialog.Show(window);
            var checkbox = Controls(dialog).OfType<CheckBox>().Single(control => control.Name == "StartWithWindows");
            var create = typeof(GemmaLauncher.App.App).GetMethod("CreateStartupMenuItem", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert(create is not null, "The tray needs a Windows startup toggle.");
            var errors = new List<string>();
            using var item = (ToolStripMenuItem)create!.Invoke(null, [shell, (Action<string>)errors.Add])!;
            Assert(!item.Checked && !checkbox.Checked, "Both controls must show that startup is disabled.");
            item.PerformClick();
            await UntilAsync(() => checkbox.Checked);
            Assert(item.Checked && startup.IsEnabled && shell.StartWithWindows, "Enabling startup from the tray must persist and appear in Settings.");
            checkbox.Checked = false;
            Assert(!item.Checked && !startup.IsEnabled, "Disabling startup in Settings must update the tray and remove the registry entry.");
            checkbox.Checked = true;
            Assert(item.Checked && startup.IsEnabled, "Enabling startup in Settings must check the tray action.");
            item.PerformClick();
            await UntilAsync(() => !checkbox.Checked);
            Assert(!item.Checked && !startup.IsEnabled && errors.Count == 0, "Disabling startup from the tray must persist and appear in Settings.");
            dialog.Close();
            window.Close();
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(keyPath, throwOnMissingSubKey: false); }
    }
    private static async Task TestLoaderSettingsSaveFailureAsync()
    {
        await using var fixture = new Fixture();
        fixture.Settings.ActiveFeature = "chatgpt";
        await using var shell = new LoaderShellViewModel(fixture.Settings, fixture.Store, () => fixture.ViewModel, () => new(new FakeAccount()));
        using var window = new LoaderWindow(shell);
        window.Show();
        await UntilAsync(() => shell.Account is not null && File.Exists(fixture.Paths.SettingsFile));
        var originalAttributes = File.GetAttributes(fixture.Paths.SettingsFile);
        try
        {
            File.SetAttributes(fixture.Paths.SettingsFile, originalAttributes | FileAttributes.ReadOnly);
            shell.CloseToTray = false;
            shell.LanguageSelection = "en";
            Assert(await shell.SelectFeatureAsync(LoaderFeature.LocalModels, default), "A settings write failure must not block navigation.");
            var warning = Controls(window).OfType<Label>().Single(label => label.Name == "SettingsSaveError");
            await UntilAsync(() => warning.Visible);
            Assert(warning.Text.StartsWith("Could not save settings.") && InViewport(warning), "An unwritable settings file must show a visible save failure instead of crashing.");
            Assert(fixture.Store.Load().CloseToTray, "A failed save must leave the previous settings file intact.");
        }
        finally { File.SetAttributes(fixture.Paths.SettingsFile, originalAttributes); }
        shell.CloseToTray = false;
        await UntilAsync(() => !Controls(window).OfType<Label>().Single(label => label.Name == "SettingsSaveError").Visible);
        Assert(!fixture.Store.Load().CloseToTray, "The next successful save must persist the settings and clear the warning.");
        shell.LanguageSelection = "ko";
        window.Close();
    }
    private static async Task TestLoaderCleanupAsync()
    {
        foreach (var hang in new[] { false, true })
        {
            await using var fixture = new Fixture();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var account = new FakeAccount { DisposeAction = () => hang ? completion.Task : Task.FromException(new IOException("fake failure")) };
            var shell = new LoaderShellViewModel(fixture.Settings,fixture.Store,() => fixture.ViewModel,() => new(account), TimeSpan.FromMilliseconds(50));
            await shell.SelectFeatureAsync(LoaderFeature.LocalModels,default); await shell.SelectFeatureAsync(LoaderFeature.ChatGpt,default);
            try { await shell.DisposeAsync(); throw new Exception("Cleanup failure was hidden"); }
            catch (InvalidOperationException) { }
            finally { completion.TrySetResult(); }
            Assert(fixture.Session.Disposed && account.DisposeStarted, "Other feature cleanup must run after failure or timeout");
        }
    }
    private sealed class FakeAccount : IAccountConnection
    {
        public int Starts;
        public bool DisposeStarted;
        public Func<Task>? DisposeAction;
        public TaskCompletionSource? StoppingGate;
        public bool IsReady { get; private set; }
        public bool IsRunning => IsReady;
        public string ApiAddress => "http://127.0.0.1:8319/v1";
        public string? LocalApiKey => IsReady ? "fake-key" : null;
        public event EventHandler? StatusChanged;
        public AccountSummary ReadSavedAccount() => new(SavedAccountState.Available, "fake@example.invalid", "test");
        public Task ConnectAsync(IProgress<ConnectionProgress>? progress, CancellationToken ct) { Starts++; IsReady = true; StatusChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public Task LoginAsync(Action<Uri> openBrowser, string language, IProgress<ConnectionProgress>? progress, CancellationToken ct) => ConnectAsync(progress, ct);
        public Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["gpt-6-luna"]);
        public async Task<AccountUsage> UsageAsync(CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); return new(DateTimeOffset.UtcNow, []); }
        public Task<ConnectionProbeResult> TestAsync(string model, CancellationToken ct) => Task.FromResult(new ConnectionProbeResult(true, model, 1));
        public async Task StopAsync(CancellationToken ct) { if (StoppingGate is not null) await StoppingGate.Task; IsReady = false; StatusChanged?.Invoke(this, EventArgs.Empty); }
        public Task SignOutAsync(CancellationToken ct) => StopAsync(ct);
        public async ValueTask DisposeAsync() { DisposeStarted = true; await StopAsync(default); if (DisposeAction is not null) await DisposeAction(); }
    }
}
