using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using GemmaLauncher.App;
using GemmaLauncher.Core;
using Localization = GemmaLauncher.Core.Localization;

namespace GemmaLauncher.UiTests;

public static class Program
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [STAThread]
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        TestBundledResources();
        Localization.Current.SetLanguage("ko");
        // Load production resources without invoking the production App's tray/mutex startup.
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = args.Length == 2 && args[0] == "--resources"
            ? args[1] : Path.Combine(Root, "src/GemmaLauncher.App/App.xaml");
        var markup = XDocument.Load(resources);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(presentation + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            new XAttribute(XNamespace.Xmlns + "loc", "clr-namespace:GemmaLauncher.App.Localization;assembly=GemmaLauncher"),
            markup.Root!.Element(presentation + "Application.Resources")!.Elements());
        application.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(application.Dispatcher));
        Exception? failure = null;
        void StopDispatcher()
        {
            if (!application.Dispatcher.HasShutdownStarted)
                application.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }
        application.DispatcherUnhandledException += (_, eventArgs) =>
        {
            failure ??= eventArgs.Exception;
            eventArgs.Handled = true;
            StopDispatcher();
        };
        if (args.Contains("--check-dispatcher-failure"))
        {
            // Exercise the boundary used by WPF callbacks outside an awaited test case.
            application.Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("Intentional dispatcher failure.")));
        }
        else
        application.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                if (args.Contains("--render-library")) { await RenderLibraryAsync(); return; }
                await TestSelectionTransactionAsync();
                await TestLibrarySelectionFlowAsync();
                await TestEmptyLibraryAsync();
                if (args.Contains("--selection-flow")) return;
                await TestScanAndLayoutAsync();
                await TestWindowsFitSmallWorkAreasAsync();
                await TestRunningContextAndRestartAsync();
                await TestCancelledStartAsync();
                await TestDisposalAwaitsCleanupAsync();
                await TestModelSwitchKeepsContextVisibleAsync();
                await TestConnectModelFileAsync();
                await TestModelFileActionsAsync();
                await TestCancelledModelFileConnectionAsync();
                await TestDisposalDuringModelFileConnectionAsync();
                await TestLanguageChangesDuringDownloadAndRunAsync();
                await TestLibraryAndPresetsAsync();
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                StopDispatcher();
            }
        }));
        Dispatcher.Run();
        if (failure is null)
        {
            Console.WriteLine("WPF controller and binding tests passed.");
            return 0;
        }
        Console.Error.WriteLine(failure);
        return 1;
    }

    private static void TestBundledResources()
    {
        var expected = CatalogLoader.Load(Path.Combine(Root, "src/GemmaLauncher.App/Assets/catalog.json"));
        var bundled = BundledResources.LoadCatalog();
        Assert(bundled.Models.Length == 5, "A standalone launcher must contain Gemma and HY-MT2 presets.");
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

    private static async Task TestScanAndLayoutAsync()
    {
        await using var fixture = new Fixture(blockScan: true);
        var vm = fixture.ViewModel;
        Assert(vm.IsBusy && !vm.CanSelectModel && !vm.PrimaryCommand.CanExecute(null), "Initial scan must disable model changes and start.");
        Assert(!vm.ConnectModelFileCommand.CanExecute(null) && !vm.ChooseModelFolderCommand.CanExecute(null) && !vm.ImportCatalogCommand.CanExecute(null), "File/folder/catalog changes must wait for the scan.");
        var first = vm.SelectedModel;
        vm.SelectedModel = vm.Models[1];
        Assert(ReferenceEquals(first, vm.SelectedModel), "A direct model selection must not race the initial scan.");

        using var bindingErrors = new BindingErrors();
        var window = new MainWindow { DataContext = vm };
        try
        {
            Assert(window.Width == 1160 && window.Height == 960, "MainWindow must have room for the expanded settings panel.");
            Assert(Math.Abs(window.FontSize - 14) < .01 && window.FontFamily.Source.Contains("Malgun Gothic", StringComparison.Ordinal),
                $"MainWindow did not inherit the intended typography: {window.FontSize}, {window.FontFamily.Source}.");
            Layout(window);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bindingErrors.AssertNone();
            fixture.Installer.Scan.TrySetResult();
            await UntilAsync(() => !vm.IsBusy);
            Assert(vm.CanSelectModel && vm.PrimaryLabel == "서버 켜기", "Verified existing files should enable a ready start.");
            var startGate = fixture.Session.BlockNextStart();
            vm.PrimaryCommand.Execute(null);
            await UntilAsync(() => fixture.Session.Status.State == ServerState.Starting);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Layout(window);
            bindingErrors.AssertNone();
            var progressBars = Visuals(window.Content as DependencyObject).OfType<ProgressBar>().ToArray();
            Assert(progressBars.Length > 0, "The real window's progress indicator must be materialized.");
            Assert(progressBars.Any(bar => Math.Abs(bar.Value - 37) < .01), "Installation progress must reach the rendered progress indicator.");
            startGate.TrySetResult();
            await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
            Layout(window);
            bindingErrors.AssertNone();
            vm.PrimaryCommand.Execute(null);
            await UntilAsync(() => !vm.IsBusy && !vm.IsServerRunning);
        }
        finally { window.Close(); }
    }

    private static async Task TestWindowsFitSmallWorkAreasAsync()
    {
        await using var fixture = new Fixture();
        await UntilAsync(() => !fixture.ViewModel.IsBusy);
        foreach (var isManager in new[] { false, true })
        {
            Window window = isManager ? new ModelManagerWindow(fixture.ViewModel) : new MainWindow { DataContext = fixture.ViewModel };
            try
            {
                var original = new Size(window.Width, window.Height);
                var originalMinimum = new Size(window.MinWidth, window.MinHeight);
                WindowLayout.FitToWorkArea(window, new Rect(0, 0, 2560, 1440));
                Assert(window.Width == original.Width && window.Height == original.Height &&
                    window.MinWidth == originalMinimum.Width && window.MinHeight == originalMinimum.Height,
                    "A large work area must preserve each window's standard size and minimum size.");

                var smallWorkArea = new Rect(0, 0, 800, 600);
                WindowLayout.FitToWorkArea(window, smallWorkArea);
                Assert(window.Width <= smallWorkArea.Width - 24 && window.Height <= smallWorkArea.Height - 24,
                    "Both windows must fit inside a small work area with their outer margin.");
                Assert(window.MinWidth <= window.Width && window.MinHeight <= window.Height,
                    "Window minimum sizes must not prevent the work-area clamp.");
                Layout(window);
                if (isManager)
                {
                    var border = (Border)window.Content;
                    var grid = (Grid)border.Child;
                    var footer = grid.Children.OfType<Border>().Single(child => Grid.GetRow(child) == 2);
                    var footerBottom = footer.TranslatePoint(new Point(0, footer.ActualHeight), border).Y;
                    Assert(footer.ActualHeight > 0 && footerBottom <= window.Height,
                        "The model browser footer must remain inside the visible window on a small work area.");
                    Assert(grid.Children.OfType<ScrollViewer>().Single().ScrollableHeight > 0,
                        "The model list must scroll when its cards cannot fit in the reduced work area.");
                }
            }
            finally { window.Close(); }
        }
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
        var nextContext = vm.ContextChoices.First(choice => choice.Value != firstContext);
        vm.SelectedContext = nextContext;
        Assert(vm.HasPendingContext && vm.RestartCommand.CanExecute(null), "Changed context must require restart.");
        Assert(fixture.Session.Starts.Count == 1 && fixture.Session.Starts[0].Context == firstContext, "Selecting context must not mutate the active server configuration.");
        Assert(fixture.Store.Load().Contexts[vm.SelectedModel!.Id] == nextContext.Value, "Context choice must be persisted.");
        vm.RestartCommand.Execute(null);
        await UntilAsync(() => fixture.Session.Starts.Count == 2 && vm.IsServerRunning && !vm.IsBusy);
        Assert(fixture.Session.Starts[1].Context == nextContext.Value && !vm.HasPendingContext, "Restart must apply the pending context exactly once.");
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

    private static async Task TestModelSwitchKeepsContextVisibleAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        foreach (var model in vm.Models.Take(3))
        {
            vm.UseLibraryModelCommand.Execute(model);
            await UntilAsync(() => !vm.IsBusy);
        }
        var window = new MainWindow { DataContext = vm };
        try
        {
            Layout(window);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var combos = Visuals(window.Content as DependencyObject).OfType<ComboBox>().ToArray();
            var models = combos.Single(combo => combo.Name == "RunModelSelector");
            var contexts = combos.Single(combo => combo.DisplayMemberPath == "Label");
            // E2B and E4B have equal default values, but each rebuilt list owns new items.
            foreach (var index in new[] { 1, 0, 1, 2, 0 })
            {
                models.SelectedValue = vm.Models[index];
                await UntilAsync(() => !vm.IsBusy);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Layout(window);
                Assert(ReferenceEquals(contexts.SelectedItem, vm.SelectedContext) && contexts.SelectedIndex >= 0,
                    "Replacing the model's context list must select an item belonging to the new list.");
                Assert(vm.SelectedContext!.Value == vm.SelectedModel!.Profile.DefaultContext,
                    "A model with no saved setting must display its own recommended context.");
                Assert(Visuals(contexts).OfType<TextBlock>().Any(block => block.Text == vm.SelectedContext.Label),
                    "The selected text capacity must remain visibly rendered after switching models.");
                var settingsScroll = ((LauncherWorkspace)window.FindName("Workspace")).ExecutionScroll;
                Assert(settingsScroll.ScrollableHeight < 1, $"Default settings should fit without scrolling: {settingsScroll.ScrollableHeight} DIP overflow.");
            }
            var saved = vm.ContextChoices.First(choice => choice.Value != vm.SelectedModel!.Profile.DefaultContext);
            contexts.SelectedItem = saved;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            models.SelectedValue = vm.Models[2];
            await UntilAsync(() => !vm.IsBusy);
            models.SelectedValue = vm.Models[0];
            await UntilAsync(() => !vm.IsBusy);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Layout(window);
            Assert(vm.SelectedContext!.Value == saved.Value && contexts.SelectedItem is ContextChoice selected && selected.Value == saved.Value,
                "Returning to a model must restore and display its saved text capacity.");
        }
        finally { window.Close(); }
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

    private static async Task TestModelFileActionsAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        using var bindingErrors = new BindingErrors();
        var window = new MainWindow { DataContext = vm };
        var manager = new ModelManagerWindow(vm);
        try
        {
            foreach (var surface in new Window[] { window, manager })
            {
                if (ReferenceEquals(surface, window)) { vm.PageIndex = 1; vm.ModelPageIndex = 2; }
                Layout(surface);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var buttons = Visuals(surface.Content as DependencyObject).OfType<Button>().ToArray();
                Assert(buttons.Count(button => ReferenceEquals(button.Command, vm.ConnectModelFileCommand)) == 1,
                    "Each model surface must offer one clear GGUF connection action.");
                Assert(buttons.Single(button => ReferenceEquals(button.Command, vm.ConnectModelFileCommand)).Content?.ToString() == Localization.T("ui.files.connect"),
                    "The main model action must describe connecting a GGUF file.");
                if (ReferenceEquals(surface, manager)) Assert(buttons.Single(button => ReferenceEquals(button.Command, vm.ChooseModelFolderCommand)).Content?.ToString() == Localization.T("ui.files.find"),
                    "Folder discovery must have its own clearly named secondary action.");
                var advanced = Visuals(surface.Content as DependencyObject).OfType<Expander>()
                    .Single(expander => expander.Header?.ToString() == Localization.T("ui.files.advanced"));
                Assert(!advanced.IsExpanded && advanced.Content is Button catalog && ReferenceEquals(catalog.Command, vm.ImportCatalogCommand) &&
                    catalog.Content?.ToString() == Localization.T("ui.files.catalog"),
                    "JSON catalog import must be explicit and contained in a collapsed advanced section.");
            }
            var oldModel = vm.SelectedModel;
            var count = vm.Models.Count;
            await vm.ConnectModelFileAsync(Path.Combine(fixture.Paths.Root, "invalid-catalog.json"));
            Assert(fixture.Installer.MatchCalls.Count == 0 && ReferenceEquals(oldModel, vm.SelectedModel) && vm.Models.Count == count &&
                fixture.Settings.ModelFiles.Count == 0 && vm.StatusDescription == Localization.T("ui.error.ggufNotRecognized"),
                "The GGUF flow must reject JSON without invoking file matching or changing the catalog or selection.");
            Layout(manager);
            Assert(Visuals(manager.Content as DependencyObject).OfType<TextBlock>().Any(block => block.Text == vm.StatusDescription && block.Visibility == Visibility.Visible),
                "The model browser must display connection errors while its modal window remains open.");
            await vm.ConnectModelFileAsync(Path.Combine(fixture.Paths.Root, "unsupported.gguf"));
            Assert(fixture.Installer.MatchCalls.Count == 1 && ReferenceEquals(oldModel, vm.SelectedModel) && fixture.Settings.ModelFiles.Count == 0,
                "An unrecognized GGUF must leave registered models, paths and selection unchanged.");
            bindingErrors.AssertNone();
        }
        finally { manager.Close(); window.Close(); }
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

            var manager = new ModelManagerWindow(vm);
            try
            {
                Layout(manager);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var cancel = Visuals(manager.Content as DependencyObject).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.PrimaryCommand));
                Assert(cancel.IsEnabled && cancel.Content?.ToString() == vm.PrimaryLabel,
                    "The modal model browser must provide its own working cancellation action.");
                cancel.Command.Execute(null);
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

    private static async Task TestLanguageChangesDuringDownloadAndRunAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        using var bindingErrors = new BindingErrors();
        var window = new MainWindow { DataContext = vm };
        var manager = new ModelManagerWindow(vm);
        try
        {
            Layout(window);
            Layout(manager);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var picker = Visuals(window.Content as DependencyObject).OfType<ComboBox>()
                .Single(combo => combo.SelectedValuePath == "Code");
            Assert(picker.Items.Count == 18, "The picker must include Windows automatic selection and CatBridge's 17 languages.");
            var model = vm.SelectedModel;
            var context = vm.SelectedContext;
            var koreanDescription = vm.SelectedPresentation!.Description;
            picker.SelectedValue = "en";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(vm.LanguageSelection == "en" && window.Title == "Gemma Model Launcher", "Changing the real picker must update window bindings immediately.");
            Assert(vm.SelectedPresentation.Description != koreanDescription, "Model descriptions must update with the interface.");
            Assert(fixture.Store.Load().LanguagePreference == "en", "Manual selection must be saved.");

            var download = fixture.Installer.BlockRuntimeDownload();
            vm.PrimaryCommand.Execute(null);
            await UntilAsync(() => vm.IsBusy && Math.Abs(vm.ProgressPercent - 37) < .01);
            Layout(window);
            Assert(vm.StatusDescription.Contains("fixture-runtime.zip", StringComparison.Ordinal), "The active download must show its file name.");
            Assert(Visuals(window.Content as DependencyObject).OfType<TextBlock>().Count(block => block.Text == vm.StatusDescription) == 1,
                "The download message must appear exactly once next to the progress bar.");
            picker.SelectedValue = "ko";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(vm.StatusDescription == Localization.T("engine.install.downloading", "fixture-runtime.zip"), "An existing download message must switch language immediately.");
            Assert(vm.IsBusy && vm.ProgressPercent == 37 && fixture.Session.Starts.Count == 0,
                "Changing language must preserve the active download and its progress.");
            Assert(ReferenceEquals(model, vm.SelectedModel) && ReferenceEquals(context, vm.SelectedContext), "Language changes must not rebuild model or context selection.");
            download.TrySetResult();
            await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
            vm.SelectedContext = vm.ContextChoices.First(choice => choice.Value != context!.Value);
            var pendingContext = vm.SelectedContext;
            foreach (var language in Localization.Current.Languages)
            {
                picker.SelectedValue = language.Code;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Layout(window);
                Layout(manager);
                Assert(vm.LanguageSelection == language.Code && (string?)picker.SelectedValue == language.Code, "The picker must retain its value after translated options refresh.");
                Assert(window.Language.IetfLanguageTag.Equals(language.Code, StringComparison.OrdinalIgnoreCase), "WPF text must use the selected language.");
                Assert(vm.PrimaryLabel == Localization.T("ui.action.stop") && window.Title == Localization.T("ui.app.title"), "Running controls and window title must update in every language.");
                Assert(fixture.Session.Starts.Count == 1 && fixture.Session.StopCalls == 0 && vm.HasPendingContext,
                    "Changing language must not restart or stop the server, or apply a pending context change.");
                Assert(ReferenceEquals(model, vm.SelectedModel) && ReferenceEquals(pendingContext, vm.SelectedContext), "Active model and pending context must stay selected.");
                Assert(Visuals(manager.Content as DependencyObject).OfType<TextBlock>().Count(block => block.Text == "Custom by Drugged Cat") == 1,
                    "The model browser must keep the 12B credit in every language.");
                bindingErrors.AssertNone();
            }
            picker.SelectedValue = "auto";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(vm.LanguageSelection == "auto" && fixture.Store.Load().LanguagePreference == "auto", "The Windows automatic preference must remain selectable and saved.");
        }
        finally
        {
            manager.Close();
            window.Close();
            Localization.Current.SetLanguage("ko");
        }
    }

    private static void Layout(Window window)
    {
        var size = new Size(window.Width, window.Height);
        window.ApplyTemplate();
        window.Measure(size);
        window.Arrange(new Rect(size));
        window.UpdateLayout();
        // A hidden Window can skip its own layout; arrange its real content to exercise all bindings.
        if (window.Content is FrameworkElement content)
        {
            content.Measure(size);
            content.Arrange(new Rect(size));
            content.UpdateLayout();
        }
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
        var window = new MainWindow { DataContext = vm };
        try
        {
            Layout(window); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var selector = Visuals(window.Content as DependencyObject).OfType<ComboBox>().Single(combo => combo.Name == "RunModelSelector");
            selector.SelectedValue = previous;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Layout(window);
            Assert(vm.SelectedModel == candidate.Definition && ReferenceEquals(selector.SelectedValue, candidate.Definition),
                "A rejected change in the real Run dropdown must restore its visible selection to the actual model.");
        }
        finally { window.Close(); }
    }

    private static async Task TestLibrarySelectionFlowAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        Assert(vm.LibraryModels.Count == 1 && fixture.Store.Load().LibraryModelIds.Contains(vm.SelectedModel!.Id),
            "A saved selection from an older launcher must migrate into the shared registry.");
        var original = vm.SelectedModel;
        var family = vm.Families.Single(item => item.Name == "HY-MT2");
        vm.PageIndex = 1;
        family.Selected = family.Variants.Last();
        Assert(vm.SelectedModel == original && vm.LibraryModels.Count == 1 && !vm.ShowPrimaryAction && !vm.ShowOperation,
            "Browsing variants must not register or select them, nor show an idle start action on Models.");
        using var errors = new BindingErrors();
        var window = new MainWindow { DataContext = vm };
        try
        {
            Layout(window); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var start = Visuals(window.Content as DependencyObject).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.PrimaryCommand));
            Assert(start.Visibility == Visibility.Collapsed, "The real Models view must hide the idle start button.");
            var variants = Visuals(window.Content as DependencyObject).OfType<ListBox>().Single(list => ReferenceEquals(list.ItemsSource, family.Variants));
            Assert(variants.Items.Count == 2 && variants.SelectedItem == family.Selected, "Both HY variants must be visible in a single selection control.");
            fixture.Installer.MissingModels.Add(family.Selected.Definition.Id);
            vm.UseLibraryModelCommand.Execute(family.Selected);
            await UntilAsync(() => !vm.IsBusy);
            Layout(window); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var selector = Visuals(window.Content as DependencyObject).OfType<ComboBox>().Single(combo => combo.Name == "RunModelSelector");
            Assert(ReferenceEquals(selector.ItemsSource, vm.LibraryModels) && ReferenceEquals(selector.SelectedValue, family.Selected.Definition) && selector.Items.Count == 2,
                "Run must use exactly the same stable registry as My models and visibly select the explicit choice.");
            Assert(vm.SelectedPresentation!.FileState == ModelFileState.DownloadNeeded && vm.PrimaryLabel == "다운로드하고 시작",
                "An undownloaded registered model must remain selectable and expose download-and-start.");
            var download = fixture.Installer.BlockRuntimeDownload();
            vm.PrimaryCommand.Execute(null);
            await UntilAsync(() => vm.IsBusy);
            vm.PageIndex = 2;
            Assert(vm.ShowOperation && vm.ShowPrimaryAction && vm.ShowOperationModel && vm.PrimaryLabel == "준비 취소",
                "A download must keep its actual model and cancel action visible across pages.");
            vm.PrimaryCommand.Execute(null);
            await UntilAsync(() => !vm.IsBusy);
            Assert(!vm.ShowPrimaryAction && fixture.Session.Starts.Count == 0, "Cancellation must not start the model or expose an unrelated idle action.");
            download.TrySetResult();
            vm.PageIndex = 0; vm.PrimaryCommand.Execute(null);
            await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
            vm.PageIndex = 1; family.Selected = family.Variants.First();
            vm.UseLibraryModelCommand.Execute(family.Selected);
            Assert(vm.SelectedModel!.Id == "hy-mt2-7b-q4-k-m" && vm.ShowPrimaryAction && vm.PrimaryLabel == "서버 끄기" && vm.ShowOperationModel,
                "Browsing during execution must retain the running model and its global stop action.");
            vm.PrimaryCommand.Execute(null); await UntilAsync(() => !vm.IsBusy && !vm.IsServerRunning);
            errors.AssertNone();
        }
        finally { window.Close(); }
    }

    private static async Task TestEmptyLibraryAsync()
    {
        await using var fixture = new Fixture(fresh: true);
        var vm = fixture.ViewModel;
        Assert(vm.PageIndex == 1 && vm.SelectedModel is null && !vm.HasLibraryModels && !vm.PrimaryCommand.CanExecute(null) && fixture.Installer.FindCalls.Count == 0,
            "First launch must begin with model browsing without a hidden default selection or file scan.");
        vm.PageIndex = 0;
        var window = new MainWindow { DataContext = vm };
        try
        {
            using var errors = new BindingErrors(); Layout(window); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); errors.AssertNone();
            Assert(vm.ShowRunEmpty && !vm.ShowOperation && Visuals(window.Content as DependencyObject).OfType<TextBlock>().Any(text => text.Text == Localization.T("ui.library.emptyTitle")),
                "Empty Run must explain how to choose a model.");
        }
        finally { window.Close(); }
        var path = Path.Combine(fixture.Paths.Root, "local-only.gguf");
        WriteLocalGguf(path);
        await vm.ConnectModelFileAsync(path);
        var card = vm.SelectedPresentation!;
        Assert(vm.LibraryModels.Count == 1 && card.FileState == ModelFileState.Ready, "A local import must be registered, selected and verified together.");
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
        Assert(vm.SelectedModel is null && !vm.HasLibraryModels && !vm.PrimaryCommand.CanExecute(null) && fixture.Store.Load().SelectedModelId is null && File.Exists(path + ".moved"),
            "Removing the last registered model must clear selection without choosing an unregistered catalog model or deleting the original file.");
    }

    private static async Task TestLibraryAndPresetsAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        Assert(vm.Families.Count == 3, "The catalog must render model families, not one permanent view per model.");
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
        foreach (var language in Localization.Current.Languages)
        {
            Localization.Current.SetLanguage(language.Code);
            foreach (var view in new[] { 0, 1, 2 })
            {
                vm.PageIndex = view;
                var window = new MainWindow { DataContext = vm };
                try { using var errors = new BindingErrors(); Layout(window); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); errors.AssertNone(); }
                finally { window.Close(); }
            }
        }
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

    private static async Task RenderLibraryAsync()
    {
        await using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        var window = new MainWindow { DataContext = vm };
        var directory = Path.Combine(Root, "artifacts/implementation/screenshots");
        Directory.CreateDirectory(directory);
        try
        {
            vm.PageIndex = 1;
            vm.Families.Single(family => family.Name == "HY-MT2").Selected = vm.Families.Single(family => family.Name == "HY-MT2").Variants.Last();
            await Capture("models");
            vm.ModelPageIndex = 2; await Capture("add-model");
            vm.PageIndex = 0;
            fixture.Installer.MissingModels.Add("hy-mt2-7b-q4-k-m");
            vm.SelectedModel = vm.Models.Single(model => model.Id == "hy-mt2-7b-q4-k-m");
            await UntilAsync(() => !vm.IsBusy);
            vm.SelectedPreset = vm.Presets.Single(preset => preset.Id == "translation");
            await Capture("run");
            vm.PageIndex = 1; vm.ModelPageIndex = 1; await Capture("my-models");
            vm.PageIndex = 2;
            vm.Glossary.Add(new() { Source = "Asterfall", Target = "아스터폴" });
            vm.Glossary.Add(new() { Source = "Moon Shard", Target = "달의 파편" });
            await Capture("presets");
            Console.WriteLine(directory);
        }
        finally { window.Close(); }
        async Task Capture(string name)
        {
            Layout(window); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Layout(window);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render((Visual)window.Content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
        }
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject? root)
    {
        if (root is null) yield break;
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, index))) yield return child;
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
            if (!blockScan) Installer.Scan.TrySetResult();
            Store = new SettingsStore(Paths);
            ViewModel = new(catalog, Paths, Settings, Store, Installer, Session);
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
        private void Set(ServerState state) { Status = new(state, state.ToString()); StatusChanged?.Invoke(this, Status); }
    }

    private sealed class BindingErrors : TraceListener
    {
        private readonly List<string> _errors = [];
        private readonly SourceLevels _previousLevel;

        public BindingErrors()
        {
            _previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(this);
        }
        public override void Write(string? message) { if (message is not null) _errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
        public void AssertNone()
        {
            if (_errors.Count > 0) throw new InvalidOperationException("WPF binding errors: " + string.Join("\n", _errors));
        }
        protected override void Dispose(bool disposing)
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            PresentationTraceSources.DataBindingSource.Switch.Level = _previousLevel;
            base.Dispose(disposing);
        }
    }
}
