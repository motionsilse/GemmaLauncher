using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using GemmaLauncher.App;
using GemmaLauncher.Core;
using Localization = GemmaLauncher.Core.Localization;
namespace GemmaLauncher.UiTests;
public static partial class Program
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Contains("--metrics-child")) { Thread.Sleep(180000); return 0; }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        using var context = new ApplicationContext();
        using var dispatch = new Control();
        dispatch.CreateControl();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Exception? failure = null;
        Application.ThreadException += (_, e) => { Console.Error.WriteLine(e.Exception); failure = e.Exception; context.ExitThread(); };
        dispatch.BeginInvoke(async () =>
        {
            try
            {
                if (args.Contains("--check-dispatcher-failure")) throw new InvalidOperationException("Intentional dispatcher failure.");
                TestBundledResources();
                if (args.Contains("--loader-tests")) { await TestLoaderAsync(); return; }
                if (args.Contains("--render-loader")) { await RenderLoaderAsync(); return; }
                if (args.Contains("--account-first-use-smoke")) { await AccountFirstUseAsync(); return; }
                Localization.Current.SetLanguage("ko");
                if (args.Length == 2 && args[0] == "--startup-benchmark") { await StartupBenchmarkAsync(args[1]); return; }
                if (args.Length == 3 && args[0] == "--startup-child") { await StartupChildAsync(args[1], long.Parse(args[2])); return; }
                if (args.Contains("--first-download-smoke")) { await FirstDownloadSmokeAsync(); return; }
                if (args.Length == 2 && args[0] == "--benchmark") { await BenchmarkAsync(args[1]); return; }
                if (args.Length == 2 && args[0] == "--graphics-probe") { await GraphicsProbeAsync(args[1]); return; }
                if (args.Contains("--render")) { await RenderWorkspaceAsync(); return; }
                await TestLoaderAsync();
                Console.WriteLine("TestDirectCatalogAsync");
                await TestDirectCatalogAsync();
                Console.WriteLine("TestUnsavedPresetIsNotRegisteredAsync");
                await TestUnsavedPresetIsNotRegisteredAsync();
                Console.WriteLine("TestPresetDraftLifecycleAsync");
                await TestPresetDraftLifecycleAsync();
                Console.WriteLine("TestDialogStopPreservesDraftAsync");
                await TestDialogStopPreservesDraftAsync();
                Console.WriteLine("TestRecoveryRoutingAsync");
                await TestRecoveryRoutingAsync();
                Console.WriteLine("TestRunningContextAndRestartAsync");
                await TestRunningContextAndRestartAsync();
                Console.WriteLine("TestCancelledStartAsync");
                await TestCancelledStartAsync();
                Console.WriteLine("TestDisposalAwaitsCleanupAsync");
                await TestDisposalAwaitsCleanupAsync();
                Console.WriteLine("TestConnectModelFileAsync");
                await TestConnectModelFileAsync();
                Console.WriteLine("TestCancelledModelFileConnectionAsync");
                await TestCancelledModelFileConnectionAsync();
                Console.WriteLine("TestDisposalDuringModelFileConnectionAsync");
                await TestDisposalDuringModelFileConnectionAsync();
                Console.WriteLine("TestSelectionTransactionAsync");
                await TestSelectionTransactionAsync();
                Console.WriteLine("TestEmptyLibraryAsync");
                await TestEmptyLibraryAsync();
                Console.WriteLine("TestLibraryAndPresetsAsync");
                await TestLibraryAndPresetsAsync();
                Console.WriteLine("TestNativeControlsAsync");
                await TestNativeControlsAsync();
                Console.WriteLine("TestLibrarySelectionFlowAsync");
                await TestLibrarySelectionFlowAsync();
                Console.WriteLine("TestLanguageAndLayoutAsync");
                await TestLanguageAndLayoutAsync();
                Console.WriteLine("TestTelemetryVisibilityAsync");
                await TestTelemetryVisibilityAsync();
            }
            catch (Exception ex) { failure = ex; }
            finally { context.ExitThread(); }
        });
        Application.Run(context);
        if (failure is not null) { Console.Error.WriteLine(failure); return 1; }
        Console.WriteLine("WinForms controller and native-control tests passed.");
        return 0;
    }
    private static IEnumerable<Control> Controls(Control parent)
    {
        yield return parent;
        foreach (Control child in parent.Controls)
            foreach (var control in Controls(child)) yield return control;
    }
    private static async Task RenderWorkspaceAsync()
    {
        await using var fixture = new Fixture(fresh: true); var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        using var window = new MainWindow(vm); window.Show(); await Task.Delay(80);
        window.Size = new System.Drawing.Size(1040, 840);
        var directory = Path.Combine(Root, "artifacts/ux-redesign"); Directory.CreateDirectory(directory);
        async Task CaptureAsync(Form form, string file)
        {
            await Task.Delay(150);
            using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
            bitmap.Save(Path.Combine(directory, file));
        }
        await CaptureAsync(window, "main.png");
        vm.SelectedModel = vm.Models.Single(model => model.Engine == "anis"); await UntilAsync(() => !vm.IsBusy);
        await CaptureAsync(window, "anis.png");
        vm.SelectedModel = vm.Models[0]; await UntilAsync(() => !vm.IsBusy);
        using (var settings = window.CreateSettingsDialog())
        {
            settings.Show(window); await CaptureAsync(settings, "settings.png");
            vm.CloseToTray = false; await CaptureAsync(settings, "settings-exit.png");
            Assert(Controls(settings).Any(control => control.Text == Localization.T("ui.settings.closeExits")) && !Controls(settings).Any(control => control.Text == Localization.T("ui.footer.exit")), "The close-window explanation must match the actual preference.");
            vm.CloseToTray = true; settings.Close();
        }
        using (var capture = new System.Windows.Forms.Timer { Interval = 150 })
        {
            capture.Tick += async (_, _) =>
            {
                capture.Stop(); var dialog = Application.OpenForms.OfType<LauncherDialog>().Single();
                await CaptureAsync(dialog, "load-options.png");
                vm.FlashAttention = "off"; await CaptureAsync(dialog, "load-options-validation.png");
                vm.FlashAttention = "on"; dialog.Close();
            };
            capture.Start(); Controls(window).OfType<Button>().Single(control => control.Name == "AdvancedToggle").PerformClick();
        }
        vm.SelectedPreset = vm.Presets.Single(p => p.Id == "translation");
        vm.Glossary.Add(new() { Source = "Traveler", Target = "여행자" });
        foreach (var page in new[] { 0, 1, 2 })
        {
            vm.PageIndex = page; await Task.Delay(150);
            await CaptureAsync(window, $"page-{page}.png");
        }
        Controls(window).OfType<TabControl>().Single(control => control.Name == "PresetEditorTabs").SelectedIndex = 1;
        await CaptureAsync(window, "glossary.png");
        vm.EditingPreset = vm.Presets.Single(preset => preset.Id == "creative");
        await CaptureAsync(window, "preset-inactive.png");
        vm.EditingPreset = vm.SelectedPreset;
        vm.PageIndex = 0; vm.SelectedModel = vm.Models.First(model => model.Id.StartsWith("hy-mt2")); await UntilAsync(() => !vm.IsBusy);
        vm.SelectedPreset = vm.Presets.Single(preset => preset.Id == "translation");
        vm.SelectedModel = vm.Models[0]; await UntilAsync(() => !vm.IsBusy); vm.PageIndex = 2;
        await CaptureAsync(window, "preset-shared.png");
        window.Size = new System.Drawing.Size(760, 600); await CaptureAsync(window, "preset-shared-compact.png");
        vm.PageIndex = 0; window.Size = new System.Drawing.Size(760, 600);
        await CaptureAsync(window, "compact.png");
        window.Size = new System.Drawing.Size(1040, 840); window.Scale(new System.Drawing.SizeF(1.5f, 1.5f));
        WindowLayout.FitToWorkArea(window, new System.Drawing.Rectangle(0, 0, 1440, 1000));
        await CaptureAsync(window, "scaled-layout.png");
        window.Close();
        await using var dialogs = new Fixture(); var editing = dialogs.ViewModel; await UntilAsync(() => !editing.IsBusy);
        editing.ResolvePresetEdits = null; editing.ConfirmPresetDeletion = null;
        using var editor = new MainWindow(editing); editor.Show(); editing.PageIndex = 2; await Task.Delay(50);
        async Task CaptureModalAsync(Action open, string file)
        {
            var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timer = new System.Windows.Forms.Timer { Interval = 150 };
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                try
                {
                    var dialog = Application.OpenForms.OfType<LauncherDialog>().Single();
                    await CaptureAsync(dialog, file); dialog.DialogResult = DialogResult.Cancel; dialog.Close(); captured.TrySetResult();
                }
                catch (Exception error) { captured.TrySetException(error); }
            };
            timer.Start(); open(); await captured.Task;
        }
        editing.PresetPrompt += "\nUnfinished draft";
        await CaptureModalAsync(() => Controls(editor).OfType<Button>().Single(control => control.Name == "BackToLauncher").PerformClick(), "unsaved-dialog.png");
        Assert(editing.PageIndex == 2 && editing.HasPresetChanges, "Keep editing must preserve both the screen and draft.");
        await CaptureModalAsync(() => Controls(editor).OfType<Button>().Single(control => ReferenceEquals(control.Tag, editing.DeletePresetCommand)).PerformClick(), "delete-dialog.png");
        editing.ResolvePresetEdits = () => PresetEditDecision.Discard;
        editing.PageIndex = 1; await editing.ConnectModelFileAsync(Path.Combine(dialogs.Paths.Root, "invalid.json"));
        await CaptureAsync(editor, "import-error.png");
        editor.Close();
    }
    private static async Task TestDirectCatalogAsync()
    {
        await using var fresh = new Fixture(fresh: true);
        Assert(fresh.ViewModel.PageIndex == 0, "First launch must show the model selection workspace directly.");
        await using var migrated = new Fixture();
        var vm = migrated.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        using var window = new MainWindow(vm); window.Show(); await Task.Delay(40);
        var selector = Controls(window).Single(c => c.Name == "RunModelSelector");
        var count = selector is ListBox list ? list.Items.Count : ((ComboBox)selector).Items.Count;
        Assert(count == vm.Models.Count, "Every bundled model must remain discoverable even with only one previously registered model.");
        window.Close();
    }
    private static async Task TestUnsavedPresetIsNotRegisteredAsync()
    {
        await using var fixture = new Fixture(); var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        var count = vm.Presets.Count; var applied = vm.SelectedPreset;
        vm.NewPresetCommand.Execute(null);
        Assert(vm.Presets.Count == count && fixture.Settings.Presets.Count == count - 1 && vm.SelectedPreset == applied,
            "A new preset must remain an editor draft until it is explicitly saved and applied.");
    }
    private static void TestBundledResources()
    {
        Assert(!typeof(MainWindow).Assembly.GetReferencedAssemblies().Any(name => name.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase"), "The launcher must not reference WPF assemblies.");
        var expected = CatalogLoader.Load(Path.Combine(Root, "src/GemmaLauncher.App/Assets/catalog.json"));
        var bundled = BundledResources.LoadCatalog();
        Assert(bundled.Models.Length == 6 && bundled.Models[^1].Engine == "anis", "A standalone launcher must contain all five models and ANIS last.");
        Assert(JsonSerializer.Serialize(expected, CatalogLoader.JsonOptions) == JsonSerializer.Serialize(bundled, CatalogLoader.JsonOptions),
            "Bundling must preserve every model/runtime URL, hash, profile and display field from the source catalog.");
        Assert(typeof(BundledResources).Assembly.GetManifestResourceNames().Contains("GemmaLauncher.Catalog.json"),
            "The default catalog must be an assembly resource, independent of an adjacent Assets directory.");
        Assert(BundledResources.ValidateLocales() == 17, "A standalone launcher must contain every locale domain and valid placeholders.");
        Assert(BundledResources.ReadNotices().Contains("MIT License", StringComparison.Ordinal),
            "License text must remain available without an adjacent notices file.");

        var root = Path.Combine(Root, ".superpowers/launcher-package-tests", Guid.NewGuid().ToString("N"));
        var report = Path.Combine(root, "invalid-command.json");
        Assert(BundledResources.RunUtilityCommand(["--verify-package", report, "--invalid"]) == 1,
            "Invalid package commands must return a failure code rather than throwing an unhandled exception.");
        using var result = JsonDocument.Parse(File.ReadAllText(report));
        Assert(!result.RootElement.GetProperty("success").GetBoolean(), "Failed verification must write an explicit unsuccessful report.");
        Assert(Directory.GetFileSystemEntries(root).Length == 1,
            "Package verification must not create model, runtime or settings data alongside its report.");
    }


    private static async Task TestRunningContextAndRestartAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        vm.PrimaryCommand.Execute(null);
        await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
        Assert(!vm.CanSelectModel && vm.CanChangeContext && vm.CanOpenWebUi, "Running state must lock model choice while allowing a pending context change.");
        var firstContext = fixture.Session.Starts.Single().Context;
        vm.FlashAttention = "off";
        Assert(vm.FlashAttention == "on" && vm.LoadOptionsFeedback.Length > 0, "An incompatible setting must preserve the valid value and explain the rejection in its dialog.");
        var nextContext = vm.ContextChoices.First(choice => choice.Value != firstContext);
        vm.SelectedContext = nextContext;
        Assert(vm.HasPendingContext && vm.RestartCommand.CanExecute(null), "Changed context must require restart.");
        Assert(fixture.Session.Starts.Count == 1 && fixture.Session.Starts[0].Context == firstContext, "Selecting context must not mutate the active server configuration.");
        Assert(fixture.Store.Load().Contexts[vm.SelectedModel!.Id] == nextContext.Value, "Context choice must be persisted.");
        vm.GpuLayers = "0";
        Assert(vm.LoadOptionsFeedback.Length == 0, "A valid change must clear the previous validation explanation.");
        Assert(vm.GpuLayers == "0" && fixture.Session.Starts[0].LoadOptions?.GpuLayers != "0", "Running load options must queue without changing the active session.");
        vm.RestartCommand.Execute(null);
        await UntilAsync(() => fixture.Session.Starts.Count == 2 && vm.IsServerRunning && !vm.IsBusy);
        Assert(fixture.Session.Starts[1].Context == nextContext.Value && !vm.HasPendingContext, "Restart must apply the pending context exactly once.");
        Assert(fixture.Session.Starts[1].LoadOptions?.GpuLayers == "0", "Restart must apply pending load options.");
        vm.GpuLayers = "auto";
        Assert(vm.HasPendingContext && vm.RestartCommand.CanExecute(null), "A load-option change alone must expose the restart action.");
        vm.GpuLayers = "0";
        Assert(!vm.HasPendingContext, "Restoring active options must clear the pending change.");
        Assert(fixture.Session.StopCalls >= 1, "Restart must first stop the owned session.");
        vm.PrimaryCommand.Execute(null);
        await UntilAsync(() => !vm.IsServerRunning && !vm.IsBusy);
        Assert(vm.CanSelectModel && !vm.CanOpenWebUi && !vm.HasPendingContext, "Stopped state must restore selection and disable stale connection actions.");
        vm.SelectedModel = vm.Models[1];
        await UntilAsync(() => !vm.IsBusy);
        Assert(vm.ApiModelId == vm.Models[1].ApiModelId, "Selecting a new stopped model must update the connection model id.");
        Assert(fixture.Store.Load().SelectedModelId == vm.Models[1].Id, "Selected model must be persisted.");
    }


    private static async Task TestCancelledStartAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        fixture.Session.BlockNextStart();
        vm.PrimaryCommand.Execute(null);
        await UntilAsync(() => fixture.Session.Status.State == ServerState.Starting);
        Assert(vm.PrimaryLabel == "준비 취소", "A starting server must expose cancellation.");
        vm.PrimaryCommand.Execute(null);
        await UntilAsync(() => !vm.IsBusy);
        Assert(fixture.Session.CancelledStarts == 1 && fixture.Session.Status.State == ServerState.Stopped, "Cancelled startup must clean up its session.");
        Assert(!vm.IsServerRunning && vm.CanSelectModel && vm.PrimaryCommand.CanExecute(null), "Cancellation must leave the launcher usable.");
        vm.PrimaryCommand.Execute(null);
        await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
        Assert(fixture.Session.Starts.Count == 2, "Cancellation must allow a subsequent start.");
        await vm.DisposeAsync();
        Assert(fixture.Session.Disposed && fixture.Session.Status.State == ServerState.Stopped, "Disposing a running controller must dispose and stop its owned session.");
    }


    private static async Task TestDisposalAwaitsCleanupAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        fixture.Session.BlockNextStart();
        var cleanup = fixture.Session.BlockNextStop();
        vm.PrimaryCommand.Execute(null);
        await UntilAsync(() => fixture.Session.Status.State == ServerState.Starting);
        var disposing = vm.DisposeAsync().AsTask();
        await UntilAsync(() => fixture.Session.StopCalls > 0);
        Assert(!disposing.IsCompleted, "Disposal must await cancellation cleanup.");
        Assert(!vm.CanSelectModel && !vm.PrimaryCommand.CanExecute(null), "A disposing controller must reject new work.");
        cleanup.TrySetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(fixture.Session.Disposed && fixture.Installer.Disposed && fixture.Session.Status.State == ServerState.Stopped,
            "Disposal must release the session and installer after cleanup finishes.");
    }


    private static async Task TestConnectModelFileAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        foreach (var index in new[] { 1, 0, 2 })
        {
            var model = vm.Models[index];
            var context = model.Profile.ContextSizes.Last();
            fixture.Settings.Contexts[model.Id] = context;
            fixture.Installer.MatchedModel = model;
            var path = Path.Combine(fixture.Paths.Root, "my-renamed-" + index + ".GGUF");
            var scans = fixture.Installer.FindCalls.Count;
            await vm.ConnectModelFileAsync(path);
            var main = model.Artifacts.Single(artifact => artifact.Role == "model");
            var saved = fixture.Store.Load();
            Assert(ReferenceEquals(vm.SelectedModel, model) && vm.ApiModelId == model.ApiModelId,
                "Connecting a renamed catalog GGUF must select the exact registered model and API identity.");
            Assert(vm.SelectedContext?.Value == context && ReferenceEquals(vm.SelectedModel!.Profile, model.Profile),
                "A connected GGUF must retain its registered profile and saved context.");
            Assert(fixture.Installer.MatchCalls.Last() == Path.GetFullPath(path) && saved.ModelFiles[main.Sha256] == Path.GetFullPath(path) && saved.SelectedModelId == model.Id,
                "Connecting a GGUF must persist its full original path by pinned hash and save the matching selection.");
            Assert(fixture.Installer.FindCalls.Count == scans + 1 && fixture.Installer.FindCalls.Last() == model.Id,
                "Every connection must resolve the matching main and companion files.");
            Assert(fixture.Session.Starts.Count == 0 && !vm.IsServerRunning,
                "Connecting an existing file must not start the server.");
        }
        var selected = vm.SelectedModel!;
        var selectedContext = vm.SelectedContext;
        var repeatScans = fixture.Installer.FindCalls.Count;
        var replacement = Path.Combine(fixture.Paths.Root, "same-model-new-location.gguf");
        await vm.ConnectModelFileAsync(replacement);
        Assert(fixture.Installer.FindCalls.Count == repeatScans + 1 && ReferenceEquals(vm.SelectedContext, selectedContext),
            "Reconnecting the selected model must rescan its files without resetting the visible context choice.");
        Assert(vm.PrimaryLabel == Localization.T("ui.action.start") && vm.StatusNote.Contains(selected.DisplayName, StringComparison.Ordinal),
            "A verified connection must show readiness and identify the connected model.");
        Assert(!File.Exists(Path.Combine(fixture.Paths.Root, "catalog.user.json")),
            "Connecting model files must not invoke JSON catalog import.");
        vm.PrimaryCommand.Execute(null);
        await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
        var launch = fixture.Session.Starts.Single();
        Assert(launch.Model.ModelPath == replacement && ReferenceEquals(launch.Model.Definition, selected) && launch.Context == selectedContext!.Value,
            "An explicit later start must use the latest connected path, registered profile and saved context.");
        Assert(!vm.ConnectModelFileCommand.CanExecute(null), "A running server must disable file connection.");
        var matchCount = fixture.Installer.MatchCalls.Count;
        await vm.ConnectModelFileAsync(Path.Combine(fixture.Paths.Root, "while-running.gguf"));
        Assert(fixture.Installer.MatchCalls.Count == matchCount, "The direct connection entry point must also reject changes while running.");
    }


    private static async Task TestCancelledModelFileConnectionAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        var originalModel = vm.SelectedModel;
        var originalContext = vm.SelectedContext;
        var target = vm.Models[1];
        var main = target.Artifacts.Single(artifact => artifact.Role == "model");
        var originalPath = Path.Combine(fixture.Paths.Root, "previous.gguf");
        fixture.Settings.ModelFiles[main.Sha256] = originalPath;
        fixture.Store.Save(fixture.Settings);
        await vm.ConnectModelFileAsync(Path.Combine(fixture.Paths.Root, "unrecognized-with-existing-mapping.gguf"));
        Assert(ReferenceEquals(originalModel, vm.SelectedModel) && fixture.Settings.ModelFiles[main.Sha256] == originalPath &&
            fixture.Store.Load().ModelFiles[main.Sha256] == originalPath,
            "An unsupported file must preserve an already saved model-file mapping and selection.");
        fixture.Installer.MatchedModel = target;

        fixture.Installer.BlockMatch();
        var matching = vm.ConnectModelFileAsync(Path.Combine(fixture.Paths.Root, "cancel-during-hash.gguf"));
        Assert(vm.IsBusy && !vm.ConnectModelFileCommand.CanExecute(null) && !vm.CanSelectModel,
            "Hash verification must be a tracked busy operation that locks competing selection changes.");
        vm.PrimaryCommand.Execute(null);
        await matching;
        Assert(ReferenceEquals(originalModel, vm.SelectedModel) && fixture.Store.Load().ModelFiles[main.Sha256] == originalPath,
            "Cancellation while hashing must preserve the old mapping and selection.");

        fixture.Installer.BlockNextScan();
        var scanning = vm.ConnectModelFileAsync(Path.Combine(fixture.Paths.Root, "cancel-during-companion-scan.gguf"));
        await UntilAsync(() => fixture.Installer.FindCalls.Last() == target.Id);
        try
        {
            vm.LanguageSelection = "en";
            Assert(fixture.Store.Load().ModelFiles[main.Sha256] == originalPath,
                "Saving another setting during companion verification must not persist the tentative file mapping.");
            vm.SelectedModel = vm.Models[2];
            var calls = fixture.Installer.MatchCalls.Count;
            await vm.ConnectModelFileAsync(Path.Combine(fixture.Paths.Root, "competing.gguf"));
            Assert(ReferenceEquals(originalModel, vm.SelectedModel) && fixture.Installer.MatchCalls.Count == calls,
                "A busy connection must reject both direct model selection and another connection.");

            var manager = new MainWindow(vm);
            try
            {
                manager.Show();
                await Task.Delay(30);
                var cancel = Controls(manager).OfType<Button>().Single(button => ReferenceEquals(button.Tag, vm.PrimaryCommand));
                Assert(cancel.Enabled && cancel.Text == vm.PrimaryLabel,
                    "The model browser must retain the global cancellation action.");
                cancel.PerformClick();
                await scanning;
            }
            finally { manager.Close(); }
            var saved = fixture.Store.Load();
            Assert(ReferenceEquals(originalModel, vm.SelectedModel) && ReferenceEquals(originalContext, vm.SelectedContext) &&
                fixture.Settings.ModelFiles[main.Sha256] == originalPath && saved.ModelFiles[main.Sha256] == originalPath && saved.SelectedModelId == originalModel!.Id,
                "Cancellation during companion verification must roll back tentative paths and retain the selected model/context.");
            Assert(saved.LanguagePreference == "en" && fixture.Session.Starts.Count == 0 && fixture.Session.StopCalls == 0 && vm.CanSelectModel,
                "File cancellation must preserve unrelated setting changes and restore an idle launcher without touching its server.");
        }
        finally { vm.LanguageSelection = "ko"; }
    }


    private static async Task TestDisposalDuringModelFileConnectionAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        var original = vm.SelectedModel;
        fixture.Installer.MatchedModel = vm.Models[2];
        var match = fixture.Installer.BlockMatch(ignoreCancellation: true);
        var connecting = vm.ConnectModelFileAsync(Path.Combine(fixture.Paths.Root, "late-match.gguf"));
        var disposing = vm.DisposeAsync().AsTask();
        Assert(!disposing.IsCompleted && !vm.ConnectModelFileCommand.CanExecute(null),
            "Disposal must await the tracked connection and reject new work.");
        match.TrySetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        await connecting;
        Assert(ReferenceEquals(vm.SelectedModel, original) && fixture.Settings.ModelFiles.Count == 0 && fixture.Store.Load().SelectedModelId == original!.Id,
            "A late hash result returned after disposal must never publish a path or stale model selection.");
        Assert(fixture.Installer.Disposed && fixture.Session.Disposed, "Disposal must release owned resources after the connection finishes.");
    }


    private static async Task TestSelectionTransactionAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        var previous = vm.SelectedModel;
        var candidate = vm.Families.Single(family => family.Name == "HY-MT2").Variants.Last();
        vm.SelectedPreset = vm.Presets.Single(preset => preset.Id == "translation");
        vm.PresetName = "";
        vm.PageIndex = 1;
        vm.UseLibraryModelCommand.Execute(candidate);
        Assert(vm.PageIndex == 1 && vm.SelectedModel == previous && !fixture.Settings.LibraryModelIds.Contains(candidate.Definition.Id),
            "Rejected preset edits must not register a candidate or navigate to a different model's Run screen.");
        vm.PresetName = "Translation";
        vm.UseLibraryModelCommand.Execute(candidate);
        await UntilAsync(() => !vm.IsBusy);
        Assert(vm.PageIndex == 0 && vm.SelectedModel == candidate.Definition && fixture.Store.Load().LibraryModelIds.Contains(candidate.Definition.Id),
            "An explicit successful choice must persist registration and open Run.");
        vm.PageIndex = 1;
        vm.UseLibraryModelCommand.Execute(candidate);
        await UntilAsync(() => !vm.IsBusy);
        Assert(vm.PageIndex == 0 && fixture.Settings.LibraryModelIds.Count(id => id == candidate.Definition.Id) == 1,
            "Reselecting the current model must open Run without duplicate registration.");
        vm.PageIndex = 1;
        var unregistered = vm.ModelCards.Single(card => card.Definition.Id == "hy-mt2-1.8b-q4-k-m");
        Directory.CreateDirectory(fixture.Paths.SettingsFile + ".tmp");
        try
        {
            vm.UseLibraryModelCommand.Execute(unregistered);
            Assert(vm.SelectedModel == candidate.Definition && vm.PageIndex == 1 && !fixture.Settings.LibraryModelIds.Contains(unregistered.Definition.Id),
                "A settings write failure must roll back selection and registration before navigating.");
        }
        finally { Directory.Delete(fixture.Paths.SettingsFile + ".tmp"); }
        await vm.ConnectModelFileAsync(Path.Combine(fixture.Paths.Root, "invalid.txt"));
        Assert(vm.HasError, "The failed file import must expose a recoverable error.");
        vm.PageIndex = 1; vm.UseLibraryModelCommand.Execute(candidate);
        await UntilAsync(() => !vm.IsBusy);
        Assert(!vm.HasError && vm.StatusNote.Length == 0 && vm.PageIndex == 0,
            "A successful retry of the same selection must clear stale errors and notices.");
        vm.PageIndex = 0; vm.SelectedPreset = vm.Presets.Single(preset => preset.Id == "translation"); vm.PresetName = "";
        var window = new MainWindow(vm);
        try
        {
            window.Show(); await Task.Delay(30);
            var selector = Controls(window).OfType<ListBox>().Single(combo => combo.Name == "RunModelSelector");
            selector.SelectedItem = vm.ModelCards.Single(card => card.Definition == previous);
            await Task.Delay(30); window.Show();
            Assert(vm.SelectedModel == candidate.Definition && ReferenceEquals(selector.SelectedItem, candidate),
                "A rejected change in the real Run dropdown must restore its visible selection to the actual model.");
        }
        finally { window.Close(); }
    }


    private static async Task TestEmptyLibraryAsync()
    {
        await using var fixture = new Fixture(fresh: true);
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        var initial = vm.SelectedModel;
        Assert(vm.PageIndex == 0 && initial?.Id == vm.Models[0].Id && vm.PrimaryCommand.CanExecute(null) && fixture.Session.Starts.Count == 0,
            "First launch must explain the default model without automatically downloading or starting it.");
        vm.PageIndex = 0;
        var window = new MainWindow(vm);
        try
        {
            window.Show(); await Task.Delay(30);
            Assert(!vm.ShowRunEmpty && vm.ShowOperation && Controls(window).OfType<ModelChoiceList>().Single().Items.Count == vm.Models.Count,
                "First launch must show every choice and the default model's preparation action.");
        }
        finally { window.Close(); }
        var path = Path.Combine(fixture.Paths.Root, "local-only.gguf");
        WriteLocalGguf(path);
        await vm.ConnectModelFileAsync(path);
        var card = vm.SelectedPresentation!;
        Assert(vm.LibraryModels.Count == 2 && card.FileState == ModelFileState.Ready, "A local import must be registered, selected and verified together.");
        // Move only the fixture file; the launcher must request reconnection and retain its registration.
        File.Move(path, path + ".moved");
        fixture.Installer.MissingModels.Add(card.Definition.Id);
        vm.PageIndex = 1; vm.UseLibraryModelCommand.Execute(card);
        await UntilAsync(() => !vm.IsBusy);
        Assert(card.FileState == ModelFileState.ReconnectRequired && vm.PrimaryLabel == "GGUF 파일 연결" && vm.HasLibraryModels,
            "A missing local file must remain registered and offer reconnection instead of a fictitious download.");
        Directory.CreateDirectory(fixture.Paths.SettingsFile + ".tmp");
        try
        {
            vm.RemoveLibraryModelCommand.Execute(card);
            Assert(vm.SelectedPresentation == card && vm.HasLibraryModels && fixture.Settings.UserModels.Count == 1,
                "A failed removal save must keep the registered model and its current selection intact.");
        }
        finally { Directory.Delete(fixture.Paths.SettingsFile + ".tmp"); }
        vm.RemoveLibraryModelCommand.Execute(card);
        await UntilAsync(() => !vm.IsBusy);
        Assert(vm.SelectedModel == initial && vm.HasLibraryModels && vm.PrimaryCommand.CanExecute(null) && fixture.Store.Load().SelectedModelId == initial!.Id && File.Exists(path + ".moved"),
            "Removing a user model must restore the remaining built-in selection and retain the original file.");
    }


    private static async Task TestLibraryAndPresetsAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        Assert(vm.Families.Count == 4, "The catalog must retain Gemma, HY-MT2 and ANIS families without hiding their individual models.");
        var hy = vm.Families.Single(family => family.Name == "HY-MT2");
        Assert(hy.Variants.Count == 2, "HY-MT2 must offer only 1.8B and 7B.");
        var lightweightCopy = hy.Selected.Headline;
        hy.Selected = hy.Variants.Last();
        Assert(hy.Selected.Headline != lightweightCopy && hy.Selected.Description.Contains("정확성"), "Variant choice must explain speed/quality, not only storage size.");
        vm.UseLibraryModelCommand.Execute(hy.Selected);
        await UntilAsync(() => !vm.IsBusy);
        Assert(vm.PageIndex == 0 && vm.SelectedModel == hy.Selected.Definition && !vm.HasMtp, "HY selection must reach Run without inheriting MTP.");
        vm.GpuLayers = "16"; vm.CacheType = "q8_0";
        Assert(fixture.Store.Load().LoadOptions[vm.SelectedModel!.Id].GpuLayers == "16", "Load settings must persist independently per model.");
        vm.SelectedPreset = vm.Presets.Single(preset => preset.Id == "translation");
        vm.PresetName = "Document translation"; vm.PresetPrompt = "Keep all information."; vm.PresetTemperature = "0.4";
        vm.Glossary.Add(new() { Source = "moon", Target = "달" }); vm.SavePresetCommand.Execute(null);
        var saved = fixture.Store.Load().Presets.Single(preset => preset.Id == "translation");
        Assert(saved.Prompt == vm.PresetPrompt && saved.Glossary.Single().Target == "달" && saved.Temperature == .4, "Prompt, glossary and generation settings must survive reload.");
        vm.PresetPrompt = "Keep the latest instructions.";
        vm.SelectedPreset = vm.Presets.Single(preset => preset.Id == "creative");
        Assert(fixture.Store.Load().Presets.Single(preset => preset.Id == "translation").Prompt == "Keep the latest instructions.", "Switching presets must not lose edits.");
        var path = Path.Combine(fixture.Paths.Root, "custom-model.gguf");
        WriteLocalGguf(path);
        fixture.Installer.MatchedModel = null;
        await vm.ConnectModelFileAsync(path);
        Assert(vm.SelectedModel?.Origin == "local" && vm.LibraryModels.Any(card => card.Definition.Id == vm.SelectedModel.Id), "An arbitrary GGUF must enter the same library.");
        var model = vm.SelectedModel!;
        Assert(fixture.Store.Load().UserModels.Single().Id == model.Id && fixture.Store.Load().SelectedModelId == model.Id, "Imported model selection and definition must persist together.");
        vm.SelectedPreset = vm.Presets.Single(preset => preset.Id == "translation");
        vm.PrimaryCommand.Execute(null);
        await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
        var launch = fixture.Session.Starts.Last();
        Assert(launch.EnableGateway && launch.Preset?.Prompt == "Keep the latest instructions." && launch.Model.MtpPath is null, "A real launch request must carry its preset, gateway and compatible model options.");
        vm.PrimaryCommand.Execute(null); await UntilAsync(() => !vm.IsBusy && !vm.IsServerRunning);
        vm.RemoveLibraryModelCommand.Execute(vm.ModelCards.Single(card => card.Definition.Id == model.Id));
        Assert(File.Exists(path) && !vm.Models.Any(item => item.Id == model.Id), "Removing a user model must retain its original GGUF.");
        // Native language/layout coverage reuses one window, as users do when changing language.
        Localization.Current.SetLanguage("ko");
    }


    private static void WriteLocalGguf(string path)
    {
        using var stream = File.Create(path); using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write(0x46554747u); writer.Write(3u); writer.Write(1UL); writer.Write(4UL);
        Text("general.architecture"); writer.Write(8u); Text("llama");
        Text("general.name"); writer.Write(8u); Text("Custom model");
        Text("llama.context_length"); writer.Write(4u); writer.Write(8192u);
        Text("tokenizer.chat_template"); writer.Write(8u); Text("{% for message in messages %}{{ message.content }}{% endfor %}");
        Text("token_embd.weight"); writer.Write(2u); writer.Write(2UL); writer.Write(2UL); writer.Write(0u); writer.Write(0UL);
        while (stream.Position % 32 != 0) writer.Write((byte)0); writer.Write(new byte[16]);
        void Text(string value) { var bytes = System.Text.Encoding.UTF8.GetBytes(value); writer.Write((ulong)bytes.Length); writer.Write(bytes); }
    }


    private static async Task UntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("UI state did not settle within five seconds.");
            await Task.Delay(10);
        }
    }


    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public FakeInstaller Installer { get; }
        public FakeSession Session { get; } = new();
        public LauncherPaths Paths { get; }
        public LauncherSettings Settings { get; } = new() { LanguagePreference = "ko" };
        public SettingsStore Store { get; }
        public LauncherViewModel ViewModel { get; }

        public Fixture(bool blockScan = false, bool fresh = false)
        {
            Paths = new LauncherPaths(Path.Combine(Root, ".superpowers/launcher-ui-tests", Guid.NewGuid().ToString("N")));
            var catalog = BundledResources.LoadCatalog();
            if (!fresh) Settings.SelectedModelId = catalog.Models[0].Id;
            Installer = new FakeInstaller(Paths.Root, Settings.ModelFiles);
            if (fresh) foreach (var model in catalog.Models) Installer.MissingModels.Add(model.Id);
            if (!blockScan) Installer.Scan.TrySetResult();
            Store = new SettingsStore(Paths);
            ViewModel = new(catalog, Paths, Settings, Store, Installer, Session);
            ViewModel.ResolvePresetEdits = () => PresetEditDecision.Save;
            ViewModel.ConfirmPresetDeletion = _ => true;
        }

        public ValueTask DisposeAsync() => ViewModel.DisposeAsync();
    }

    private sealed class FakeInstaller(string root, IReadOnlyDictionary<string, string> modelFiles) : IInstallationService, IDisposable
    {
        public TaskCompletionSource Scan { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _runtimeDownload;
        private TaskCompletionSource? _nextScan;
        private TaskCompletionSource? _nextMatch;
        private bool _ignoreMatchCancellation;
        public ModelDefinition? MatchedModel { get; set; }
        public List<string> FindCalls { get; } = [];
        public HashSet<string> MissingModels { get; } = [];
        public List<string> MatchCalls { get; } = [];
        public TaskCompletionSource BlockRuntimeDownload() => _runtimeDownload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BlockNextScan() => _nextScan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BlockMatch(bool ignoreCancellation = false)
        {
            _ignoreMatchCancellation = ignoreCancellation;
            return _nextMatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public bool Disposed { get; private set; }
        public Exception? NextRuntimeFailure { get; set; }

        public async Task<InstalledModel?> FindAsync(ModelDefinition model, CancellationToken cancellationToken = default)
        {
            FindCalls.Add(model.Id);
            var gate = _nextScan; _nextScan = null;
            await Scan.Task.WaitAsync(cancellationToken);
            if (gate is not null) await gate.Task.WaitAsync(cancellationToken);
            return MissingModels.Contains(model.Id) ? null : Installed(model);
        }
        public async Task<ModelDefinition?> MatchModelFileAsync(string path, IEnumerable<ModelDefinition> models, IProgress<InstallationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            MatchCalls.Add(path);
            var gate = _nextMatch; _nextMatch = null;
            progress?.Report(new("verifying", Localization.T("engine.install.verifying", Path.GetFileName(path)), 43, 100));
            if (gate is not null)
            {
                if (_ignoreMatchCancellation) await gate.Task;
                else await gate.Task.WaitAsync(cancellationToken);
            }
            return MatchedModel;
        }
        public Task<InstalledModel> EnsureModelAsync(ModelDefinition model, IProgress<InstallationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Installed(model));
        }
        public async Task<string> EnsureRuntimeAsync(RuntimePackage runtime, IProgress<InstallationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NextRuntimeFailure is { } failure) { NextRuntimeFailure = null; throw failure; }
            progress?.Report(new("downloading", Localization.T("engine.install.downloading", "fixture-runtime.zip"), 37, 100));
            if (_runtimeDownload is not null) await _runtimeDownload.Task.WaitAsync(cancellationToken);
            await Task.Yield();
            return Path.Combine(root, "fixture-runtime.exe");
        }
        private InstalledModel Installed(ModelDefinition model) => new(model,
            modelFiles.GetValueOrDefault(model.Artifacts.Single(artifact => artifact.Role == "model").Sha256) ?? Path.Combine(root, model.Id + ".gguf"),
            model.Artifacts.Any(artifact => artifact.Role == "mtp") ? Path.Combine(root, model.Id + ".mtp.gguf") : null);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSession : IServerSession
    {
        private TaskCompletionSource? _nextStart;
        private TaskCompletionSource? _nextStop;
        public SessionStatus Status { get; private set; } = new(ServerState.Stopped, "stopped");
        public event EventHandler<SessionStatus>? StatusChanged;
        public List<LaunchConfiguration> Starts { get; } = [];
        public int StopCalls { get; private set; }
        public int CancelledStarts { get; private set; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource BlockNextStart() => _nextStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BlockNextStop() => _nextStop = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task StartAsync(LaunchConfiguration configuration, CancellationToken cancellationToken = default)
        {
            Starts.Add(configuration);
            var gate = _nextStart; _nextStart = null;
            Set(ServerState.Starting);
            try
            {
                if (gate is not null) await gate.Task.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Set(ServerState.Running);
            }
            catch (OperationCanceledException) { CancelledStarts++; Set(ServerState.Stopped); throw; }
        }
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            var gate = _nextStop; _nextStop = null;
            Set(ServerState.Stopping);
            if (gate is not null) await gate.Task;
            Set(ServerState.Stopped);
        }
        public async ValueTask DisposeAsync() { Disposed = true; await StopAsync(); }
        public int? ProcessId { get; set; }
        private void Set(ServerState state) { Status = new(state, state.ToString(), ProcessId); StatusChanged?.Invoke(this, Status); }
    }

}
