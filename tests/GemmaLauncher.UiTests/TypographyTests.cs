using GemmaLauncher.App;
using GemmaLauncher.Core;

namespace GemmaLauncher.UiTests;

public static partial class Program
{
    private static async Task TestModelListFontScalingAsync()
    {
        Localization.Current.SetLanguage("ko");
        using var normal = new Font("Segoe UI", 11.5f);
        using var enlarged = new Font("Segoe UI", 17.25f);
        using var largest = new Font("Segoe UI", 23f);
        using var form = new Form { Font = normal, ClientSize = new(420, 240) };
        using var list = new ModelChoiceList();
        list.Items.Add(new ModelPresentation(BundledResources.LoadCatalog().Models.Single(m => m.Id == "hy-mt2-1.8b-q4-k-m")));
        form.Controls.Add(list); form.Show(); await Task.Delay(30);
        var first = MeasureInk();
        form.Font = enlarged; await Task.Delay(30); var second = MeasureInk();
        form.Font = largest; await Task.Delay(30); var third = MeasureInk();
        form.Font = normal; await Task.Delay(30); var restored = MeasureInk();
        Console.WriteLine($"Owner-draw text heights (name/detail): {first}, {second}, {third}, restored={restored}; native DPI={list.DeviceDpi}");
        Assert(second.Name >= first.Name * 1.3 && second.Detail >= first.Detail * 1.3, "Model names and metadata must grow with the parent's 150% font instead of staying fixed");
        Assert(third.Name >= first.Name * 1.7 && third.Detail >= first.Detail * 1.7, "Both model text lines must follow the parent's 200% font");
        Assert(restored == first, "Returning to the original font must not accumulate scaling");
        form.Close();

        (int Name, int Detail) MeasureInk()
        {
            using var bitmap = new Bitmap(list.Width, list.Height);
            list.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var row = list.GetItemRectangle(0);
            int InkHeight(int top, int bottom)
            {
                var rows = new List<int>();
                for (var y = top; y < Math.Min(bottom, bitmap.Height); y++)
                    for (var x = row.Left + 24; x < Math.Min(row.Right - 24, bitmap.Width); x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        if (pixel.R < 180 && pixel.G < 180 && pixel.B < 180) { rows.Add(y); break; }
                    }
                return rows.Count == 0 ? 0 : rows[^1] - rows[0] + 1;
            }
            return (InkHeight(row.Top, row.Top + row.Height * 3 / 5), InkHeight(row.Top + row.Height * 3 / 5, row.Bottom - 2));
        }
    }

    private static async Task RenderTypographyAsync()
    {
        Localization.Current.SetLanguage("ko");
        await using var fixture = new Fixture(); await UntilAsync(() => !fixture.ViewModel.IsBusy);
        await using var shell = BenchmarkShell(fixture);
        using var window = new LoaderWindow(shell); window.Show();
        var directory = Path.Combine(Root, "artifacts/cat-loader/typography"); Directory.CreateDirectory(directory);
        await Capture(window, "local-100");
        fixture.ViewModel.SelectedModel = fixture.ViewModel.Models.Single(model => model.Engine == "anis");
        await UntilAsync(() => !fixture.ViewModel.IsBusy); await Capture(window, "anis-100");
        window.Size = new(720, 560); await Capture(window, "anis-small-100"); window.Size = new(1040, 840);
        fixture.ViewModel.PageIndex = 2; await Capture(window, "presets-100"); fixture.ViewModel.PageIndex = 0;
        await shell.SelectFeatureAsync(LoaderFeature.ChatGpt, default); await Capture(window, "account-100");
        using (var settings = (Form)typeof(LoaderWindow).GetMethod("SettingsDialog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null)!)
        { settings.Show(window); await Capture(settings, "settings-100"); settings.Close(); }
        window.Close();
        Console.WriteLine("Captured native typography at DPI " + window.DeviceDpi + ". Font-size transition tests are separate from physical monitor DPI tests.");
        async Task Capture(Form target, string name)
        {
            await Task.Delay(100);
            using var bitmap = new Bitmap(target.Width, target.Height);
            target.DrawToBitmap(bitmap, new Rectangle(Point.Empty, target.Size)); bitmap.Save(Path.Combine(directory, name + ".png"));
        }
    }
}
