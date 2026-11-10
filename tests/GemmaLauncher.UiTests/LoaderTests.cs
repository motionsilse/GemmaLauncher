using GemmaLauncher.App;
using GemmaLauncher.Core.Accounts;
namespace GemmaLauncher.UiTests;
public static partial class Program
{
    private static async Task TestLoaderAsync()
    {
        await using var fixture = new Fixture();
        var account = new FakeAccount();
        var paths = new GemmaLauncher.Core.LauncherPaths(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var settings = new GemmaLauncher.Core.LauncherSettings { ActiveFeature = "chatgpt" };
        var localCalls = 0;
        await using var shell = new LoaderShellViewModel(settings, new(paths), () => { localCalls++; return fixture.ViewModel; }, () => new(account));
        using var window = new LoaderWindow(shell); window.Show(); await Task.Delay(50);
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
        await Task.Delay(100);
        var localAction = Controls(window).OfType<Button>().Single(b => b.Name == "PrimaryAction");
        Assert(localAction.Visible && window.ClientRectangle.Contains(window.RectangleToClient(localAction.RectangleToScreen(localAction.ClientRectangle))), "Local primary must stay visible in unified shell");
        window.Close();
        if (Directory.Exists(paths.Root)) Directory.Delete(paths.Root, true);
    }
    private sealed class FakeAccount : IAccountConnection
    {
        public int Starts;
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
        public Task StopAsync(CancellationToken ct) { IsReady = false; StatusChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public Task SignOutAsync(CancellationToken ct) => StopAsync(ct);
        public async ValueTask DisposeAsync() => await StopAsync(default);
    }
}
