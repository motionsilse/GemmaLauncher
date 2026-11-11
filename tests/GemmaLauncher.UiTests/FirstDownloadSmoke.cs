using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Forms;
using GemmaLauncher.App;
using GemmaLauncher.Core;

namespace GemmaLauncher.UiTests;

public static partial class Program
{
    private static async Task FirstDownloadSmokeAsync()
    {
        var catalog = BundledResources.LoadCatalog();
        var folder = Path.Combine(Root, "artifacts/first-download-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        var paths = new LauncherPaths(folder); var settings = new LauncherSettings { LanguagePreference = "ko", SelectedModelId = "hy-mt2-1.8b-q4-k-m" };
        var store = new SettingsStore(paths);
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var installer = new InstallationService(paths, modelFiles: settings.ModelFiles, discoverExistingModels: false);
        await using var vm = new LauncherViewModel(catalog, paths, settings, store, installer, serverPort: port);
        await UntilLongAsync(() => !vm.IsBusy, TimeSpan.FromSeconds(30));
        Assert(!Directory.Exists(paths.Models) && !Directory.Exists(paths.Runtime), "First-use test must start with no models or engine in its private directory.");
        using var window = new MainWindow(vm); window.Show(); await Task.Delay(80);
        vm.GpuLayers = "0"; vm.CacheType = "f16"; vm.SelectedContext = vm.ContextChoices.First(choice => choice.Value == 2048);
        await Task.Delay(50);
        var button = Controls(window).OfType<Button>().Single(b => b.Name == "PrimaryAction");
        Console.WriteLine("FIRST DOWNLOAD DATA: " + folder); Console.WriteLine("CLICK: " + button.Text); Console.Out.Flush();
        Assert(vm.PrimaryLabel == Localization.T("ui.action.download"), "A missing public built-in model must offer download and run immediately.");
        button.PerformClick();
        var watch = Stopwatch.StartNew(); var reportAt = TimeSpan.Zero;
        while (vm.IsBusy)
        {
            if (watch.Elapsed > TimeSpan.FromMinutes(15)) throw new TimeoutException("First model download/start timed out.");
            if (watch.Elapsed >= reportAt)
            {
                Console.WriteLine($"{watch.Elapsed.TotalSeconds:0}s {vm.StatusDescription} {vm.ProgressPercent:0.0}%"); Console.Out.Flush(); reportAt += TimeSpan.FromSeconds(5);
            }
            await Task.Delay(200);
        }
        Assert(vm.IsServerRunning, "Download/start failed: " + vm.StatusDescription);
        using var client = new HttpClient { BaseAddress = new Uri(vm.ApiAddress + "/"), Timeout = TimeSpan.FromMinutes(3) };
        using var models = await client.GetAsync("models"); models.EnsureSuccessStatusCode();
        var request = new { model = vm.ApiModelId, messages = new[] { new { role = "system", content = "Translate the user's English text into Korean. Output only the translation." }, new { role = "user", content = "The captain has arrived. Open the gate." } }, max_tokens = 80, stream = false, temperature = 0 };
        using var completion = await client.PostAsJsonAsync("chat/completions", request); var body = await completion.Content.ReadAsStringAsync();
        Assert(completion.IsSuccessStatusCode, "Completion failed: " + body);
        using var parsed = JsonDocument.Parse(body); var output = parsed.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        Assert(!string.IsNullOrWhiteSpace(output), "The downloaded model returned no translation.");
        await File.WriteAllTextAsync(Path.Combine(folder, "first-translation.json"), body);
        Console.WriteLine("LIVE TRANSLATION: " + output); Console.Out.Flush();
        using (var bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size)); bitmap.Save(Path.Combine(folder, "running.png")); }
        button.PerformClick(); await UntilLongAsync(() => !vm.IsBusy && !vm.IsServerRunning, TimeSpan.FromSeconds(30));

        await VerifyAnisFirstUseAsync(vm, paths, button);
        window.Close();
    }

    private static async Task AnisFirstUseSmokeAsync()
    {
        Localization.Current.SetLanguage("ko");
        await using var fixture = new Fixture(fresh: true);
        var vm = fixture.ViewModel;
        await UntilLongAsync(() => !vm.IsBusy, TimeSpan.FromSeconds(30));
        using var window = new MainWindow(vm);
        window.Show();
        await Task.Delay(80);
        var button = Controls(window).OfType<Button>().Single(control => control.Name == "PrimaryAction");
        await VerifyAnisFirstUseAsync(vm, fixture.Paths, button);
        window.Close();
    }

    private static async Task AnisInstalledSmokeAsync(string modelFolder, string cacheFolder)
    {
        var catalog = BundledResources.LoadCatalog();
        var paths = new LauncherPaths(cacheFolder);
        var settings = new LauncherSettings { LanguagePreference = "ko", SelectedModelId = "anis-trans", AnisModelFolder = modelFolder };
        var store = new SettingsStore(paths);
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var vm = new LauncherViewModel(catalog, paths, settings, store, serverPort: port);
        vm.OpenAnisDownload = _ => throw new InvalidOperationException("A verified ANIS installation opened the browser.");
        await using var shell = new LoaderShellViewModel(settings, store, () => vm, () => throw new InvalidOperationException());
        using var window = new LoaderWindow(shell);
        window.Show();
        await UntilLongAsync(() => !vm.IsBusy && Controls(window).Any(control => control.Name == "PrimaryAction"), TimeSpan.FromSeconds(20));
        var start = Controls(window).OfType<Button>().Single(control => control.Name == "PrimaryAction");
        Assert(start.Text == Localization.T("ui.action.start"), "Verified ANIS files should offer to start immediately. " + start.Text);
        start.PerformClick();
        await UntilLongAsync(() => !vm.IsBusy, TimeSpan.FromMinutes(2));
        Assert(vm.IsServerRunning, "Verified ANIS failed to start: " + vm.StatusDescription);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(vm.ApiAddress + "/"), Timeout = TimeSpan.FromSeconds(10) };
        using var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = vm.ApiModelId,
            messages = new[] { new { role = "user", content = "明日は早く起きて、朝ごはんを作ります。" } },
            stream = false
        });
        response.EnsureSuccessStatusCode();
        using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var translation = parsed.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        Assert(translation?.Any(character => character is >= '가' and <= '힣') == true, "ANIS should translate into Korean using automatic direction.");
        var modelList = Controls(window).OfType<ModelChoiceList>().Single();
        Assert(modelList.Enabled, "Another model must be selectable while ANIS is running.");
        modelList.SelectedItem = vm.ModelCards[0];
        await UntilLongAsync(() => !vm.IsBusy && !vm.IsServerRunning && vm.SelectedModel == vm.Models[0], TimeSpan.FromSeconds(20));
        Assert(vm.ApiModelId == vm.Models[0].ApiModelId, "Changing model must update the shared API model ID.");
        Console.WriteLine("Real ANIS start, automatic translation and stop-on-switch passed.");
        window.Close();
    }

    private static async Task VerifyAnisFirstUseAsync(LauncherViewModel vm, LauncherPaths paths, Button button)
    {
        var browserDownloads = new List<Uri>();
        vm.OpenAnisDownload = browserDownloads.Add;
        vm.SelectedModel = vm.Models.Single(model => model.Id == "anis-trans"); await UntilLongAsync(() => !vm.IsBusy, TimeSpan.FromSeconds(30)); await Task.Delay(80);
        Assert(!Directory.Exists(Path.Combine(paths.Models, "anis-trans")), "ANIS must also begin with no model files.");
        Console.WriteLine("ANIS FIRST CLICK: " + button.Text); Console.Out.Flush(); button.PerformClick();
        Form? accessDialog = null;
        await UntilLongAsync(() =>
        {
            accessDialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => Controls(form).Any(control => control.Name == "AnisDownloadStatus"));
            return accessDialog is not null && browserDownloads.Count > 0 || (!vm.IsBusy && vm.HasError);
        }, TimeSpan.FromSeconds(60));
        Assert(accessDialog is not null, "ANIS must reach its access dialog after the actual official download response. " + vm.StatusDescription);
        Assert(!Controls(accessDialog!).OfType<TextBox>().Any(), "ANIS download must not ask users to paste an access token.");
        Assert(browserDownloads.Count == 1 && browserDownloads[0].Host == "huggingface.co" && browserDownloads[0].AbsolutePath == "/oauth/authorize",
            "First use must open one official Hugging Face authorization page instead of opening file tabs.");
        Assert(Controls(accessDialog!).OfType<Label>().Single(control => control.Name == "AnisDownloadStatus").Text == Localization.T("ui.anis.awaitingApproval") &&
               !Controls(accessDialog!).OfType<ProgressBar>().Single(control => control.Name == "AnisDownloadProgress").Visible,
            "The dialog must explicitly show that it is waiting for browser approval, without showing a download in progress.");
        using (var bitmap = new Bitmap(accessDialog!.Width, accessDialog.Height)) { accessDialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, accessDialog.Size)); bitmap.Save(Path.Combine(paths.Root, "anis-first-access.png")); }
        Console.WriteLine("ANIS first use opened one official OAuth page after a real unauthenticated request."); Console.Out.Flush();
        accessDialog.DialogResult = DialogResult.Cancel; accessDialog.Close();
        await UntilLongAsync(() => !vm.IsBusy, TimeSpan.FromSeconds(20));
        Assert(!vm.IsServerRunning && vm.CanSelectModel, "Cancelling first-use access must leave the launcher ready without an orphan server.");
    }
    private static async Task UntilLongAsync(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed > timeout) throw new TimeoutException("Native first-use state did not settle."); await Task.Delay(50); }
    }
}
