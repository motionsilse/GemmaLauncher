using GemmaLauncher.App;
using GemmaLauncher.Core.Accounts;
using GemmaLauncher.Core;
namespace GemmaLauncher.UiTests;
public static partial class Program
{
    private static async Task TestLoaderAsync()
    {
        await TestLoaderSettingsSaveFailureAsync();
        await using var fixture = new Fixture();
        var account = new FakeAccount();
        var paths = new GemmaLauncher.Core.LauncherPaths(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var settings = new GemmaLauncher.Core.LauncherSettings { ActiveFeature = "chatgpt" };
        var localCalls = 0;
        await using var shell = new LoaderShellViewModel(settings, new(paths), () => { localCalls++; return fixture.ViewModel; }, () => new(account));
        var available = new TaskCompletionSource<ReleaseUpdate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ReleaseUpdate(new Version(0, 10, 0), new Uri("https://github.com/motionsilse/GemmaLauncher/releases/tag/v0.10.0"));
        Uri? openedRelease = null;
        using var window = new LoaderWindow(shell, checkUpdate: token => available.Task.WaitAsync(token), openRelease: uri => openedRelease = uri);
        window.Show(); await Task.Delay(50);
        var update = Controls(window).OfType<Button>().Single(button => button.Name == "UpdateNotice");
        Assert(!update.Visible, "The update notice must stay hidden until a new release is found.");
        available.SetResult(release);
        await UntilAsync(() => update.Visible);
        Assert(update.Text == Localization.T("ui.update.available", "0.10.0"), "The notice must show the new version.");
        update.PerformClick();
        Assert(openedRelease == release.DownloadPage, "The notice must open that version's download page.");
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
        account.StoppingGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = vm.StopAsync(); await Task.Delay(50);
        var backgroundStop = Controls(window).OfType<Button>().Single(b => b.Text.Contains(Localization.T("ui.loader.chat")) && b.Text.Contains(Localization.T("ui.account.stopping")));
        try { Assert(!backgroundStop.Enabled, "Background stop must be disabled during cleanup"); }
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
