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

        vm.SelectedModel = vm.Models.Single(model => model.Id == "anis-trans"); await UntilLongAsync(() => !vm.IsBusy, TimeSpan.FromSeconds(30)); await Task.Delay(80);
        Assert(!Directory.Exists(Path.Combine(paths.Models, "anis-trans")), "ANIS must also begin with no model files.");
        Console.WriteLine("ANIS FIRST CLICK: " + button.Text); Console.Out.Flush(); button.PerformClick();
        Form? accessDialog = null;
        await UntilLongAsync(() =>
        {
            accessDialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => Controls(form).Any(control => control.Name == "AnisDownloadToken"));
            return accessDialog is not null || (!vm.IsBusy && vm.HasError);
        }, TimeSpan.FromSeconds(60));
        Assert(accessDialog is not null, "ANIS must reach its access dialog after the actual official download response. " + vm.StatusDescription);
        var tokenInput = Controls(accessDialog!).OfType<TextBox>().Single(control => control.Name == "AnisDownloadToken");
        Assert(tokenInput.UseSystemPasswordChar && tokenInput.Text.Length == 0, "The first-use dialog must request temporary download access without assuming credentials.");
        using (var bitmap = new Bitmap(accessDialog!.Width, accessDialog.Height)) { accessDialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, accessDialog.Size)); bitmap.Save(Path.Combine(folder, "anis-first-access.png")); }
        Console.WriteLine("ANIS access dialog reached from a real unauthenticated download. License acceptance and account credentials were not supplied by the test."); Console.Out.Flush();
        accessDialog.DialogResult = DialogResult.Cancel; accessDialog.Close();
        await UntilLongAsync(() => !vm.IsBusy, TimeSpan.FromSeconds(20));
        Assert(!vm.IsServerRunning && vm.CanSelectModel, "Cancelling first-use access must leave the launcher ready without an orphan server.");
        window.Close();
    }
    private static async Task UntilLongAsync(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed > timeout) throw new TimeoutException("Native first-use state did not settle."); await Task.Delay(50); }
    }
}
