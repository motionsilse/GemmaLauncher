using GemmaLauncher.App;
using GemmaLauncher.Core;

namespace GemmaLauncher.UiTests;
public static partial class Program
{
    private static async Task TestResourceGuidesAsync()
    {
        Localization.Current.SetLanguage("ko");
        await using var fixture = new Fixture(); var vm=fixture.ViewModel;
        await UntilAsync(()=>!vm.IsBusy);
        await using var shell = BenchmarkShell(fixture);
        using var window = new LoaderWindow(shell); window.Show(); await Task.Delay(80);
        Assert(Controls(window).Any(c=>c.Name=="RunResourceEstimate"),"Selecting a model must expose its storage and memory estimate before running.");
        var guide=Controls(window).Single(c=>c.Name=="RunResourceEstimate");
        foreach(var model in vm.Models.ToArray())
        {
            vm.SelectedModel=model; await UntilAsync(()=>!vm.IsBusy); await Task.Delay(40);
            Assert(guide.Text.Contains(model.DownloadLabel) && guide.Text.Contains("RAM") && guide.Text.Contains("VRAM"),model.Id+": show model storage, RAM and VRAM together.");
            Assert(!guide.Text.Contains(Localization.T("ui.resources.unknown")),model.Id+": all six built-in models need a reference estimate.");
            if(model.Engine!="anis") Assert(guide.Text.Contains(model.Profile.DefaultContext.ToString("N0")),"The memory reference must identify its context basis.");
            await Capture("resource-"+model.Id);
        }
        vm.SelectedModel=vm.Models.First(); await UntilAsync(()=>!vm.IsBusy);
        await UntilAsync(() => guide.Text == vm.SelectedPresentation!.ResourceGuide);
        var estimate=guide.Text; vm.PrimaryCommand.Execute(null); await UntilAsync(()=>vm.IsServerRunning && !vm.IsBusy); await Task.Delay(40);
        Assert(guide.Visible && guide.Text==estimate,"Running telemetry must not replace the pre-run resource estimate.");
        vm.PrimaryCommand.Execute(null); await UntilAsync(()=>!vm.IsServerRunning && !vm.IsBusy);
        foreach(var language in Localization.Current.Languages)
        {
            Localization.Current.SetLanguage(language.Code); await Task.Delay(25);
            Assert(guide.Text.Contains(Localization.T("ui.resources.storage",vm.SelectedModel!.DownloadLabel)),language.Code+": storage label must follow language changes.");
        }
        Localization.Current.SetLanguage("ko");
        window.Size = new(720,560); await Capture("resource-small-ko");
        var scroller = Controls(window).OfType<Panel>().Single(p => p.AutoScroll && p.Contains(guide));
        scroller.ScrollControlIntoView(guide); await Task.Delay(40);
        Assert(guide.Height >= guide.GetPreferredSize(new Size(guide.Width,0)).Height, "Narrow windows must wrap the resource description without clipping text.");
        Assert(InViewport(Controls(window).OfType<Button>().First(b => ReferenceEquals(b.Tag,vm.PrimaryCommand))), "The primary local model action must remain reachable at minimum size.");
        window.Close();
        var definition = vm.Models.First();
        var local = definition with { Resources = null, Artifacts = [definition.Artifacts[0] with { IsLocal = true }] };
        var localGuide = new ModelPresentation(local).ResourceGuide;
        Assert(localGuide.Contains(Localization.T("ui.resources.linked")) && localGuide.Contains(Localization.T("ui.resources.install", "2.0 GB")), "Existing local files must not be counted as another download or copy.");
        Assert(localGuide.Contains(Localization.T("ui.resources.unknown")), "Imported models without metadata must not inherit invented memory estimates.");
        var unknownSize = local with { Artifacts = [local.Artifacts[0] with { IsLocal = false, Bytes = 0 }] };
        var unknownGuide = new ModelPresentation(unknownSize).ResourceGuide;
        Assert(unknownGuide.Contains(Localization.T("ui.resources.sizeUnknown")) && !unknownGuide.Contains(Localization.T("ui.resources.install", "2.0 GB")), "Unknown model size must not produce a complete installation-space estimate.");
        var invalid = definition with { Resources = new() { RamMinGb = double.NaN, RamMaxGb = 3, VramMinGb = 1, VramMaxGb = 2 } };
        Assert(new ModelPresentation(invalid).ResourceGuide.Contains(Localization.T("ui.resources.unknown")), "Invalid imported resource metadata must fall back safely.");
        Console.WriteLine("Resource estimates: six models, selection, telemetry separation and 17 languages PASS.");
        async Task Capture(string name)
        {
            await Task.Delay(40); var directory=Path.Combine(Root,"artifacts/cat-loader/resource-guides"); Directory.CreateDirectory(directory);
            using var bitmap=new Bitmap(window.Width,window.Height); window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,window.Size)); bitmap.Save(Path.Combine(directory,name+".png"));
        }
    }
}
