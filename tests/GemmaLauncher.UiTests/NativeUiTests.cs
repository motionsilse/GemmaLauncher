using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using GemmaLauncher.App;
using GemmaLauncher.Core;
using Localization = GemmaLauncher.Core.Localization;

namespace GemmaLauncher.UiTests;
public static partial class Program
{
    private static async Task TestNativeControlsAsync()
    {
        await using var fixture = new Fixture(blockScan: true);
        var vm = fixture.ViewModel;
        Assert(vm.IsBusy && !vm.CanSelectModel && !vm.PrimaryCommand.CanExecute(null), "Initial scan must lock selection and start.");
        using var window = new MainWindow(vm, 0xc001, 0xc002); window.Show(); await Task.Delay(30);
        var activations = 0;
        window.ActivationRequested += (_, _) => { activations++; window.Show(); };
        foreach (var message in new[] { 0xc001, 0xc002 })
        {
            window.Hide(); SendMessage(window.Handle, message, IntPtr.Zero, IntPtr.Zero);
            Assert(window.Visible, "Both activation protocols must reach the hidden native window.");
        }
        Assert(activations == 2, "Each activation message must be handled once.");
        Assert(window.CreatedPageCount == 1, "Only the selected page should be created at startup.");
        var models = Controls(window).OfType<ComboBox>().Single(c => c.Name == "RunModelSelector");
        Assert(!models.Enabled, "The real model selector must be disabled during scanning.");
        fixture.Installer.Scan.TrySetResult(); await UntilAsync(() => !vm.IsBusy); await Task.Delay(30);
        foreach (var model in vm.Models.Take(3)) { vm.UseLibraryModelCommand.Execute(model); await UntilAsync(() => !vm.IsBusy); }
        await Task.Delay(30);
        var contexts = Controls(window).OfType<ComboBox>().Single(c => c.Name == "ContextSelector");
        foreach (var index in new[] { 1, 0, 1, 2, 0 })
        {
            models.SelectedItem = vm.ModelCards[index]; await UntilAsync(() => !vm.IsBusy); await Task.Delay(30);
            Assert(vm.SelectedModel == vm.Models[index] && contexts.SelectedItem == vm.SelectedContext && contexts.Text == vm.SelectedContext!.Label,
                "Changing the native selector must select the matching model and display its rebuilt context.");
        }
        var saved = vm.ContextChoices.Last(); contexts.SelectedItem = saved; await Task.Delay(30);
        models.SelectedItem = vm.ModelCards[2]; await UntilAsync(() => !vm.IsBusy); await Task.Delay(30);
        models.SelectedItem = vm.ModelCards[0]; await UntilAsync(() => !vm.IsBusy); await Task.Delay(30);
        Assert(vm.SelectedContext!.Value == saved.Value && contexts.Text == saved.Label, "Returning to a model must restore its saved context.");
        var gate = fixture.Session.BlockNextStart();
        var primary = Controls(window).OfType<Button>().Single(c => c.Name == "PrimaryAction"); primary.PerformClick();
        await UntilAsync(() => fixture.Session.Status.State == ServerState.Starting); await Task.Delay(30);
        Assert(Controls(window).OfType<ProgressBar>().Single().Value == 37, "Installation progress must reach the native progress bar.");
        gate.TrySetResult(); await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy); await Task.Delay(30);
        primary.PerformClick(); await UntilAsync(() => !vm.IsBusy && !vm.IsServerRunning);
        vm.PageIndex = 2; await Task.Delay(30);
        Assert(window.CreatedPageCount == 2, "Navigating to presets must create that page on demand.");
        vm.SelectedPreset = vm.Presets.Single(p => p.Id == "translation"); await Task.Delay(30);
        var grid = Controls(window).OfType<DataGridView>().Single();
        vm.Glossary.Add(new() { Source = "moon", Target = "달" }); await Task.Delay(30);
        grid.CurrentCell = grid.Rows[0].Cells[1]; grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = "달빛";
        Controls(window).OfType<Button>().Single(b => ReferenceEquals(b.Tag, vm.SavePresetCommand)).PerformClick();
        Assert(fixture.Store.Load().Presets.Single(p => p.Id == "translation").Glossary[0].Target == "달빛", "Save must commit the actively edited glossary cell.");
        grid.Rows.RemoveAt(0);
        Controls(window).OfType<Button>().Single(b => ReferenceEquals(b.Tag, vm.SavePresetCommand)).PerformClick();
        Assert(fixture.Store.Load().Presets.Single(p => p.Id == "translation").Glossary.Length == 0, "Deleting a grid row must remove the saved glossary entry.");
        vm.PageIndex = 1; vm.ModelPageIndex = 2; await Task.Delay(30);
        var connect = Controls(window).OfType<Button>().Single(b => ReferenceEquals(b.Tag, vm.ConnectModelFileCommand));
        Assert(connect.Text == Localization.T("ui.files.connect") && connect.Enabled, "Add model must offer a localized GGUF connection action.");
        Assert(Controls(window).OfType<Button>().Any(b => ReferenceEquals(b.Tag, vm.ImportCatalogCommand)), "Catalog import must remain available.");
        var old = vm.SelectedModel;
        await vm.ConnectModelFileAsync(Path.Combine(fixture.Paths.Root, "invalid.json")); await Task.Delay(30);
        Assert(vm.SelectedModel == old && fixture.Installer.MatchCalls.Count == 0 && Controls(window).OfType<Label>().Any(l => l.Visible && l.Text.Contains(vm.StatusDescription)),
            "Invalid imports must leave selection intact and show their error on the current page.");
        window.Close();
    }

    private static async Task TestLibrarySelectionFlowAsync()
    {
        await using var fixture = new Fixture(); var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        var original = vm.SelectedModel; var family = vm.Families.Single(f => f.Name == "HY-MT2");
        vm.PageIndex = 1;
        using var window = new MainWindow(vm); window.Show(); await Task.Delay(30);
        var variant = Controls(window).OfType<ComboBox>().Single(c => c.Name == "VariantSelector" && c.Items.Contains(family.Variants.Last()));
        variant.SelectedItem = family.Variants.Last(); await Task.Delay(30);
        Assert(vm.SelectedModel == original && vm.LibraryModels.Count == 1 && !vm.ShowPrimaryAction, "Browsing variants must not change execution selection.");
        fixture.Installer.MissingModels.Add(family.Selected.Definition.Id);
        var select = Controls(variant.Parent!).OfType<Button>().Single(b => ReferenceEquals(b.Tag, vm.UseLibraryModelCommand)); select.PerformClick();
        await UntilAsync(() => !vm.IsBusy); await Task.Delay(30);
        var selector = Controls(window).OfType<ComboBox>().Single(c => c.Name == "RunModelSelector");
        Assert(vm.PageIndex == 0 && selector.Items.Count == 2 && selector.SelectedItem == family.Selected && vm.SelectedPresentation!.FileState == ModelFileState.DownloadNeeded,
            "Explicit selection must register the undownloaded model and select it in Run.");
        var download = fixture.Installer.BlockRuntimeDownload(); vm.PrimaryCommand.Execute(null); await UntilAsync(() => vm.IsBusy);
        vm.PageIndex = 2; await Task.Delay(30);
        var primary = Controls(window).OfType<Button>().Single(b => b.Name == "PrimaryAction");
        Assert(primary.Visible && primary.Enabled && vm.ShowOperationModel, "Download cancellation must remain visible across pages.");
        primary.PerformClick(); await UntilAsync(() => !vm.IsBusy);
        Assert(fixture.Session.Starts.Count == 0, "Cancelling a download must not start inference.");
        download.TrySetResult(); vm.PageIndex = 0; vm.PrimaryCommand.Execute(null); await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
        vm.PageIndex = 1; vm.UseLibraryModelCommand.Execute(family.Variants.First()); await Task.Delay(30);
        Assert(vm.SelectedModel == family.Variants.Last().Definition && primary.Visible && vm.PrimaryLabel == Localization.T("ui.action.stop"), "Running model and stop control must survive browsing.");
        primary.PerformClick(); await UntilAsync(() => !vm.IsBusy && !vm.IsServerRunning); window.Close();
    }

    private static async Task TestLanguageAndLayoutAsync()
    {
        await using var fixture = new Fixture(); var vm = fixture.ViewModel; await UntilAsync(() => !vm.IsBusy);
        using var window = new MainWindow(vm); window.Show(); await Task.Delay(30);
        var picker = Controls(window).OfType<ComboBox>().Single(c => c.Name == "LanguageSelector");
        Assert(picker.Items.Count == 18, "Language picker must include automatic detection and all 17 languages.");
        var download = fixture.Installer.BlockRuntimeDownload(); vm.PrimaryCommand.Execute(null);
        await UntilAsync(() => vm.IsBusy && vm.ProgressPercent == 37);
        picker.SelectedItem = "en"; await Task.Delay(30);
        Assert(vm.StatusDescription.Contains("fixture-runtime.zip") && vm.ProgressPercent == 37, "Language changes must preserve live download progress.");
        download.TrySetResult(); await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
        vm.SelectedContext = vm.ContextChoices.Last();
        var pending = vm.SelectedContext; var model = vm.SelectedModel;
        foreach (var language in Localization.Current.Languages)
        {
            picker.SelectedItem = language.Code; await Task.Delay(30);
            Assert(window.Text == Localization.T("ui.app.title") && fixture.Store.Load().LanguagePreference == language.Code && (string?)picker.SelectedItem == language.Code,
                "Language changes must update real controls and persist without losing selection.");
            Assert(vm.SelectedModel == model && vm.SelectedContext == pending && fixture.Session.Starts.Count == 1 && fixture.Session.StopCalls == 0,
                "Language changes must not restart the server or reset pending context.");
            foreach (var page in new[] { 0, 1, 2 })
            {
                vm.PageIndex = page; await Task.Delay(20);
                Assert(!Controls(window).Any(c => c.Text.StartsWith("ui.", StringComparison.Ordinal)), "Native UI must resolve every localization key.");
            }
        }
        picker.SelectedItem = "auto"; await Task.Delay(30); Assert(fixture.Store.Load().LanguagePreference == "auto", "Automatic language selection must be saved.");
        vm.LanguageSelection = "ko"; vm.PrimaryCommand.Execute(null); await UntilAsync(() => !vm.IsBusy && !vm.IsServerRunning);
        foreach (var area in new[] { new Rectangle(0, 0, 2560, 1440), new Rectangle(0, 0, 800, 600) })
        {
            WindowLayout.FitToWorkArea(window, area); await Task.Delay(30);
            Assert(window.Width <= area.Width - 24 && window.Height <= area.Height - 24 && window.MinimumSize.Width <= window.Width,
                "The window must fit small work areas without a conflicting minimum size.");
        }
        window.Scale(new SizeF(1.5f, 1.5f));
        WindowLayout.FitToWorkArea(window, new Rectangle(0, 0, 1200, 900)); await Task.Delay(30);
        var save = Controls(window).OfType<Button>().Single(b => ReferenceEquals(b.Tag, vm.SavePresetCommand));
        var saveBounds = window.RectangleToClient(save.RectangleToScreen(save.ClientRectangle));
        Assert(window.ClientRectangle.Contains(saveBounds), "The preset save action must stay reachable after 150% layout scaling.");
        window.Scale(new SizeF(1 / 1.5f, 1 / 1.5f));
        var directory = Path.Combine(Root, "artifacts/lightweight/screenshots"); Directory.CreateDirectory(directory);
        window.Size = new Size(1080, 860);
        foreach (var page in new[] { 0, 1, 2 })
        {
            vm.PageIndex = page; await Task.Delay(60);
            using var bitmap = new Bitmap(window.Width, window.Height); window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
            bitmap.Save(Path.Combine(directory, $"page-{page}.png"));
        }
        window.Close();
    }

    private static async Task TestTelemetryVisibilityAsync()
    {
        await using var fixture = new Fixture(); var vm = fixture.ViewModel; await UntilAsync(() => !vm.IsBusy);
        fixture.Session.ProcessId = Environment.ProcessId;
        using var window = new MainWindow(vm); window.Show(); vm.PrimaryCommand.Execute(null);
        await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy); await UntilAsync(() => vm.MemoryUsage.Contains("RAM"));
        Assert(Controls(window).OfType<Label>().Any(label => label.Text == vm.MemoryUsage), "The native memory label must display new telemetry samples.");
        ProcessMetrics? Reader() => (ProcessMetrics?)typeof(LauncherViewModel).GetField("_metrics", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm);
        var reader = Reader();
        await Task.Delay(2200);
        Assert(reader is not null && ReferenceEquals(reader, Reader()), "Visible telemetry must reuse its PDH/process reader across timer ticks.");
        Assert(vm.IsTelemetryActive, "Visible Run must sample server telemetry.");
        window.Hide(); Assert(!vm.IsTelemetryActive, "Hiding must stop telemetry immediately.");
        await UntilAsync(() => Reader() is null);
        try { reader!.Read(); throw new InvalidOperationException("Hidden telemetry retained its process handle."); }
        catch (ObjectDisposedException) { }
        await Task.Delay(100); window.Show(); await Task.Delay(30); Assert(vm.IsTelemetryActive, "Restoring Run must resume telemetry.");
        for (var i = 0; i < 5; i++) { window.Hide(); window.Show(); }
        window.WindowState = FormWindowState.Minimized; Assert(!vm.IsTelemetryActive, "Minimizing must stop telemetry.");
        window.WindowState = FormWindowState.Normal; vm.PageIndex = 1; await Task.Delay(30); Assert(!vm.IsTelemetryActive, "Non-Run pages must not sample telemetry.");
        vm.PageIndex = 0; await Task.Delay(30); vm.PrimaryCommand.Execute(null); await UntilAsync(() => !vm.IsBusy && !vm.IsServerRunning);
        Assert(!vm.IsTelemetryActive, "Stopping inference must stop telemetry.");
        await vm.DisposeAsync(); Assert(!vm.IsTelemetryActive, "Disposal must stop and drain telemetry."); window.Close();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
