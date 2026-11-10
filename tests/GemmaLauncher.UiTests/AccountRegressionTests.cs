using GemmaLauncher.App;
using GemmaLauncher.Core;
using GemmaLauncher.Core.Accounts;

namespace GemmaLauncher.UiTests;

public static partial class Program
{
    private static async Task TestAccountRegressionsAsync()
    {
        var failures = new List<Exception>();
        foreach (var test in new Func<Task>[] { TestAuthenticationRecoveryAsync, TestIndependentDiagnosticsAsync, TestUnexpectedExitAsync, TestDynamicAccessibleActionsAsync })
        {
            try { await test(); Console.WriteLine(test.Method.Name + ": PASS"); }
            catch (Exception ex) { failures.Add(ex); Console.WriteLine(test.Method.Name + ": FAIL: " + ex.Message); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static async Task TestAuthenticationRecoveryAsync()
    {
        Localization.Current.SetLanguage("ko");
        var account = new RegressionAccount();
        await using var vm = new AccountConnectionViewModel(account);
        await vm.ActivateAsync(default); await vm.StartAsync();
        foreach (var (status, key) in new[] { (401,"authRequired"), (403,"accessDenied") })
        {
            account.Query = ct => AuthenticationHttpFailureAsync(status,ct);
            await vm.QueryUsageAsync();
            Assert(vm.AccountHint == Localization.T("ui.account."+key), "Authentication HTTP status must produce account recovery guidance: " + status);
            Assert(vm.SignOutCommand.CanExecute(null) && vm.IsConnected, "Recovery must retain the account and explicit logout control.");
            account.Query = _ => Task.CompletedTask;
            vm.RefreshCommand.Execute(null); await UntilAsync(() => !vm.IsQueryBusy);
            Assert(vm.AccountHint == Localization.T("ui.account."+key), "A local model listing must not clear an upstream authentication failure.");
            await vm.QueryUsageAsync();
            Assert(vm.AccountHint == Localization.T("ui.account.keepLogin"), "Successful upstream usage lookup must clear authentication guidance.");
        }
        account.Connect = ct => AuthenticationHttpFailureAsync(401,ct);
        await vm.StopAsync(); await vm.StartAsync();
        Assert(vm.StatusDescription == Localization.T("ui.account.authRequired"), "Lifecycle failures must preserve authentication status too.");
    }
    private static async Task AuthenticationHttpFailureAsync(int status,CancellationToken ct)
    {
        using var http = new HttpClient(new AuthenticationHandler(status)) { BaseAddress = new("http://127.0.0.1/v1/") };
        await new AccountApi(http).ModelsAsync(ct);
    }
    private sealed class AuthenticationHandler(int status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status));
    }

    private static async Task TestIndependentDiagnosticsAsync()
    {
        var account = new RegressionAccount();
        await using var vm = new AccountConnectionViewModel(account);
        await vm.ActivateAsync(default); await vm.StartAsync(); await vm.QueryUsageAsync();
        var usage = vm.UsageText;
        vm.TestCommand.Execute(null); await UntilAsync(() => !vm.IsQueryBusy);
        Assert(vm.UsageText == usage, "A request test must not replace account usage with its result.");
        Assert(DiagnosticText(vm) == Localization.T("ui.account.testOk"), "Request tests need a dedicated success result.");
        vm.RefreshCommand.Execute(null); await UntilAsync(() => !vm.IsQueryBusy);
        Assert(DiagnosticText(vm) == Localization.T("ui.account.modelsOk"), "Successful model checks need an explicit result.");
        account.Query = _ => Task.FromException(new ConnectionFailure("usage_unavailable"));
        await vm.QueryUsageAsync();
        Assert(DiagnosticText(vm) == Localization.T("ui.account.modelsOk"), "A usage failure must not replace the diagnostic result.");
        account.Query = _ => Task.FromException(new OperationCanceledException());
        vm.RefreshCommand.Execute(null); await UntilAsync(() => !vm.IsQueryBusy);
        Assert(DiagnosticText(vm) == Localization.T("ui.account.modelsTimeout"), "Diagnostic deadlines must be shown in diagnostics.");
        account.Query = ct => Task.Delay(Timeout.Infinite,ct);
        vm.TestCommand.Execute(null); await UntilAsync(() => vm.IsQueryBusy);
        Assert(DiagnosticText(vm) == Localization.T("ui.account.testingRequest"), "Running tests need a progress state.");
        vm.CancelQueryCommand.Execute(null); await UntilAsync(() => !vm.IsQueryBusy);
        Assert(DiagnosticText(vm) == Localization.T("ui.account.diagnosticsCancelled"), "Explicit diagnostic cancellation must be distinct from timeout.");
        await vm.StopAsync();
        Assert(DiagnosticText(vm) == Localization.T("ui.account.diagnosticsOff"), "Offline diagnostics must explain how to enable their actions.");
    }

    private static string? DiagnosticText(AccountConnectionViewModel vm) => typeof(AccountConnectionViewModel).GetProperty("DiagnosticsText")?.GetValue(vm) as string;

    private static async Task TestUnexpectedExitAsync()
    {
        var account = new RegressionAccount();
        await using var vm = new AccountConnectionViewModel(account);
        await vm.ActivateAsync(default); await vm.StartAsync();
        account.Crash(); await Task.Delay(40);
        Assert(vm.ServerStatus == Localization.T("ui.account.attention") && vm.ServerHint == Localization.T("ui.account.unexpectedExit"), "Unexpected exit must be distinct from intentional stop and explain restart.");
        Assert(vm.Account.State == SavedAccountState.Available && vm.ServerCommand.CanExecute(null), "Unexpected exit must preserve login and allow explicit restart.");
        await vm.StartAsync();
        Assert(vm.ServerStatus == Localization.T("ui.account.serverRunning"), "Restart must clear the unexpected-exit indication.");
        await vm.StopAsync();
        Assert(vm.ServerStatus == Localization.T("ui.account.serverOff") && vm.ServerHint == Localization.T("ui.account.offHint"), "Intentional stop must not look like a crash.");
    }

    private static async Task TestDynamicAccessibleActionsAsync()
    {
        var account = new JourneyAccount();
        await using var vm = new AccountConnectionViewModel(account);
        await vm.ActivateAsync(default);
        using var window = new Form { Size = new(1040,840) };
        window.Controls.Add(new AccountConnectionView(vm)); window.Show();
        vm.LoginCommand.Execute(null); await UntilAsync(() => vm.CanReopen); await Task.Delay(40);
        var login = Controls(window).OfType<Button>().Single(b => b.Name=="AccountLogin");
        Assert(login.AccessibleName == login.Text && login.AccessibilityObject.Name == login.Text && login.Text == vm.LoginLabel, "Accessible login action must say cancel while authentication is pending.");
        var labels = new List<string>(); login.TextChanged += (_,_) => labels.Add(login.Text);
        vm.MaskKey(); await Task.Delay(40);
        Assert(labels.All(text=>text==vm.LoginLabel),"An unrelated refresh must not temporarily change cancel back into sign-in.");
        account.LoginGate.TrySetResult(); await UntilAsync(() => !vm.IsBusy); await vm.StartAsync(); await Task.Delay(40);
        vm.KeyCommand.Execute(null); await Task.Delay(40);
        var key = Controls(window).OfType<Button>().Single(b => ReferenceEquals(b.Tag,vm.KeyCommand));
        Assert(key.AccessibleName == key.Text && key.AccessibilityObject.Name == key.Text && key.Text == vm.KeyLabel, "Accessible key action must say hide after reveal.");
        Assert(Controls(window).OfType<Button>().Any(b => b.AccessibleName == Localization.T("ui.account.address")+" · "+Localization.T("ui.account.copy")), "Qualified copy names must remain intact.");
        window.Close();
    }

    private sealed class RegressionAccount : IAccountConnection
    {
        public Func<CancellationToken,Task> Connect = _ => Task.CompletedTask, Query = _ => Task.CompletedTask;
        public bool IsReady { get; private set; }
        public bool IsRunning => IsReady;
        public string? FailureCode { get; private set; }
        public string ApiAddress => "http://127.0.0.1:8319/v1";
        public string? LocalApiKey => "test-local-api-key";
        public event EventHandler? StatusChanged;
        public AccountSummary ReadSavedAccount() => new(SavedAccountState.Available,"review@example.invalid","test");
        public async Task ConnectAsync(IProgress<ConnectionProgress>? p,CancellationToken ct) { await Connect(ct); FailureCode=null; IsReady=true; StatusChanged?.Invoke(this,EventArgs.Empty); }
        public Task LoginAsync(Action<Uri> browser,string lang,IProgress<ConnectionProgress>? p,CancellationToken ct) => throw new NotSupportedException();
        public async Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct) { await Query(ct); return [AccountConnection.DefaultModel]; }
        public async Task<AccountUsage> UsageAsync(CancellationToken ct) { await Query(ct); return new(DateTimeOffset.Now,[new("Codex",20,18000,null)]); }
        public async Task<ConnectionProbeResult> TestAsync(string model,CancellationToken ct) { await Query(ct); return new(true,model,0); }
        public void Crash() { IsReady=false; FailureCode="runtime_exited"; StatusChanged?.Invoke(this,EventArgs.Empty); }
        public Task StopAsync(CancellationToken ct) { IsReady=false; FailureCode=null; StatusChanged?.Invoke(this,EventArgs.Empty); return Task.CompletedTask; }
        public Task SignOutAsync(CancellationToken ct) => StopAsync(ct);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task RenderAccountRegressionsAsync()
    {
        await using var fixture = new Fixture(); fixture.Settings.ActiveFeature="chatgpt";
        var account = new RegressionAccount();
        await using var shell = new LoaderShellViewModel(fixture.Settings,fixture.Store,()=>fixture.ViewModel,()=>new(account));
        using var window = new LoaderWindow(shell); window.Show(); await Task.Delay(40);
        var vm = shell.Account!; await vm.StartAsync(); await vm.QueryUsageAsync();
        var directory = Path.Combine(Root,"artifacts/cat-loader/four-fixes-screens"); Directory.CreateDirectory(directory);
        await Capture("running");
        account.Query = ct=>AuthenticationHttpFailureAsync(401,ct); await vm.QueryUsageAsync();
        foreach(var language in Localization.Current.Languages)
        {
            Localization.Current.SetLanguage(language.Code); window.Size=new(720,560); await Task.Delay(40);
            var logout=Controls(window).Single(c=>c.Name=="AccountSignOut");
            var stop=Controls(window).Single(c=>c.Name=="GlobalAccountStop");
            if (!InViewport(logout) || !InViewport(stop)) { Save(window,"layout-failure-"+language.Code); Console.WriteLine($"logout={logout.Bounds} visible={InViewport(logout)} stop={stop.Bounds} visible={InViewport(stop)}"); }
            Assert(InViewport(logout) && InViewport(stop),language.Code+": authentication recovery must retain visible logout and stop controls.");
        }
        Localization.Current.SetLanguage("ko"); await Capture("auth-small");
        window.Size=new(1040,840); await Capture("auth");
        account.Query=_=>Task.CompletedTask; vm.RefreshCommand.Execute(null); await UntilAsync(()=>!vm.IsQueryBusy);
        using var timer=new System.Windows.Forms.Timer {Interval=60};
        var captured=false;
        timer.Tick+=(_,_)=>
        {
            var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f!=window && f.Text==Localization.T("ui.account.details"));
            if(dialog is null)return; timer.Stop();
            Assert(Controls(dialog).Any(c=>c.Name=="AccountDiagnosticsResult" && c.Text==Localization.T("ui.account.modelsOk")),"The actual diagnostic dialog must show its model-check result.");
            Save(dialog,"diagnostics"); captured=true; dialog.Close();
        };
        timer.Start(); Controls(window).OfType<Button>().Single(b=>b.Text==Localization.T("ui.account.details")).PerformClick();
        Assert(captured,"Diagnostic dialog was not captured");
        await vm.QueryUsageAsync(); account.Crash(); await Capture("unexpected-exit");
        window.Size=new(720,560); await Capture("unexpected-exit-small"); window.Close();
        async Task Capture(string name) { await Task.Delay(70); Save(window,name); }
        void Save(Form form,string name) { using var bitmap=new Bitmap(form.Width,form.Height); form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size)); bitmap.Save(Path.Combine(directory,name+".png")); }
    }
}
