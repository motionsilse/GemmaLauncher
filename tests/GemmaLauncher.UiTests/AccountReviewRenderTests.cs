using System.Reflection;
using GemmaLauncher.App;
using GemmaLauncher.Core;
using GemmaLauncher.Core.Accounts;

namespace GemmaLauncher.UiTests;

public static partial class Program
{
    private static async Task RenderAccountReviewAsync()
    {
        await using var fixture = new Fixture();
        fixture.Settings.ActiveFeature = "chatgpt";
        var account = new ReviewAccount
        {
            Usage = new(DateTimeOffset.Now, [new("Codex", 0, 2592000, DateTimeOffset.Now.AddDays(30))])
        };
        await using var shell = new LoaderShellViewModel(fixture.Settings, fixture.Store, () => fixture.ViewModel, () => new(account));
        using var window = new LoaderWindow(shell); window.Show();
        var vm = shell.Account!; await vm.StartAsync(); await vm.QueryUsageAsync();
        var directory = Path.Combine(Root, "artifacts/cat-loader/review-screens"); Directory.CreateDirectory(directory);
        foreach (var language in Localization.Current.Languages)
        {
            Localization.Current.SetLanguage(language.Code); await Task.Delay(50);
            var usageLabel = Controls(window).OfType<Label>().Single(label => label.Text == vm.UsageText);
            var scroll = Controls(window).OfType<Panel>().Single(panel => panel.AutoScroll && panel.Contains(usageLabel));
            scroll.ScrollControlIntoView(usageLabel);
            await Capture(window, "usage-" + language.Code);
        }
        Localization.Current.SetLanguage("ko");
        using (var dialog = (Form)typeof(LoaderWindow).GetMethod("SettingsDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!)
        {
            dialog.Show(window); await Capture(dialog, "settings-ko"); dialog.Close();
        }
        await vm.StopAsync();
        foreach (var code in new[] { "session_in_use", "port_in_use", "default_model_unavailable" })
        {
            account.Connect = _ => Task.FromException(new ConnectionFailure(code)); await vm.StartAsync();
            foreach (var scroll in Controls(window).OfType<Panel>().Where(panel => panel.AutoScroll)) scroll.AutoScrollPosition = Point.Empty;
            window.Size = new(720, 560); await Capture(window, code + "-small-ko");
        }
        window.Close();
        Console.WriteLine("Captured usage in 17 languages, settings, and three errors at minimum window size.");
        async Task Capture(Form form, string name)
        {
            await Task.Delay(75);
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
            image.Save(Path.Combine(directory, name + ".png"));
        }
    }
}
