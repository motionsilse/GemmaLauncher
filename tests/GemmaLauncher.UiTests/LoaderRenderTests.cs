using GemmaLauncher.App;
using GemmaLauncher.Core;
using GemmaLauncher.Core.Accounts;
namespace GemmaLauncher.UiTests;
public static partial class Program
{
    private static LoaderShellViewModel BenchmarkShell(Fixture fixture) => new(fixture.Settings, fixture.Store, () => fixture.ViewModel, () => new(new FakeAccount()));
    private static async Task RenderLoaderAsync()
    {
        await using var fixture = new Fixture();
        fixture.Settings.ActiveFeature = "chatgpt";
        await using var shell = BenchmarkShell(fixture);
        using var window = new LoaderWindow(shell); window.Show();
        var directory = Path.Combine(Root,"artifacts/cat-loader/screens"); Directory.CreateDirectory(directory);
        async Task Capture(string name)
        {
            await Task.Delay(100);
            using var bitmap = new System.Drawing.Bitmap(window.Width,window.Height);
            window.DrawToBitmap(bitmap,new System.Drawing.Rectangle(System.Drawing.Point.Empty,window.Size)); bitmap.Save(Path.Combine(directory,name+".png"));
        }
        foreach (var language in Localization.Current.Languages) { Localization.Current.SetLanguage(language.Code); await Capture("account-"+language.Code); }
        Localization.Current.SetLanguage("ko"); await shell.Account!.StartAsync(); await Capture("account-ready");
        window.Size = new(720,560); await Capture("account-small");
        window.Size = new(1040,840); await shell.SelectFeatureAsync(LoaderFeature.LocalModels,default); await Capture("local-with-account");
        await shell.SelectFeatureAsync(LoaderFeature.ChatGpt,default); window.Scale(new System.Drawing.SizeF(1.5f,1.5f)); await Capture("account-scaled");
        Assert(InViewport(Controls(window).Single(c=>c.Name=="AccountPrimary")) && InViewport(Controls(window).Single(c=>c.Name=="AccountSignOut")), "Primary account actions must stay visible at 150% programmatic scale.");
        window.Scale(new System.Drawing.SizeF(4f/3,4f/3)); await Capture("account-scaled-200");
        Assert(InViewport(Controls(window).Single(c=>c.Name=="AccountPrimary")) && InViewport(Controls(window).Single(c=>c.Name=="AccountSignOut")), "Primary account actions must stay visible at 200% programmatic scale.");
        window.Close(); Console.WriteLine("Loader native screenshots captured (17 locales, ready, small, coexistence, manual scale).");
    }
    private static async Task AccountFirstUseAsync()
    {
        var root = Path.Combine(Path.GetTempPath(),"CatLoaderFirstUse-"+Guid.NewGuid().ToString("N"));
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0); listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var connection = new AccountConnection(root,port);
        await using var vm = new AccountConnectionViewModel(connection);
        await vm.ActivateAsync(default);
        var reachedBrowser = false; var publicPortStayedClosed = false; var helperNeverReady = false;
        vm.OpenBrowser = _ => {
            reachedBrowser = true;
            using var probe = new System.Net.Sockets.TcpClient();
            try { probe.Connect("127.0.0.1", port); }
            catch (System.Net.Sockets.SocketException) { publicPortStayedClosed = true; }
            helperNeverReady = !connection.IsReady;
            vm.CancelPending();
        };
        using var form = new Form { Size = new(1040,840) }; var view = new AccountConnectionView(vm); form.Controls.Add(view); form.Show();
        var primary = Controls(form).OfType<Button>().Single(b => b.Name == "AccountLogin");
        primary.PerformClick();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (vm.IsBusy && timer.Elapsed < TimeSpan.FromMinutes(3)) await Task.Delay(100);
        Assert(reachedBrowser, "Actual first-use button must prepare official engine and reach browser handoff");
        Assert(publicPortStayedClosed && helperNeverReady, "Authentication must keep the public API closed and not ready.");
        Assert(!vm.IsRunning && !vm.IsConnected, "Cancelling browser handoff must stop owned proxy");
        Assert(connection.FailureCode is null,"Cancelling authentication must not report an unexpected server exit");
        Assert(!File.Exists(Path.Combine(root,"config.yaml")), "No live config after cancellation");
        form.Close(); await vm.DisposeAsync();
        if (Directory.Exists(root)) Directory.Delete(root,true);
        Console.WriteLine("First-use native button → official download → login handoff → cancel cleanup PASS. No real account or inference.");
    }
}
