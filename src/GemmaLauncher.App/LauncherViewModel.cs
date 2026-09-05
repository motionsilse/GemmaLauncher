using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using GemmaLauncher.Core;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

public sealed class LauncherViewModel : ObservableObject, IAsyncDisposable
{
    private readonly LauncherPaths _paths;
    private readonly SettingsStore _store;
    private readonly LauncherSettings _settings;
    private readonly ModelCatalog _catalog;
    private readonly IServerSession _session;
    private IInstallationService _installer;
    private readonly List<RelayCommand> _commands = [];
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _scanCancellation;
    private Task? _operation;
    private Task? _scan;
    private ModelDefinition? _selectedModel;
    private ContextChoice? _selectedContext;
    private InstalledModel? _installed;
    private bool _busy;
    private bool _scanning;
    private bool _isDisposed;
    private bool _rebuildingChoices;
    private bool _deferSettingsSave;
    private bool _settingsSavePending;
    private readonly System.Windows.Threading.DispatcherTimer _memoryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _readingMemory;
    private string? _memoryUsage;
    private long? _privateRamBytes;
    private Uri? _prerequisiteDownload;
    private string _error = "";
    private string _progressMessage = "";
    private string _statusNote = "";
    private double _progressPercent;
    private bool _indeterminate = true;
    private int? _activeContext;

    public ObservableCollection<ModelDefinition> Models { get; }
    public ObservableCollection<ModelPresentation> ModelCards { get; }
    public ModelPresentation? SelectedPresentation => ModelCards.FirstOrDefault(card => ReferenceEquals(card.Definition, SelectedModel));
    public IReadOnlyList<LanguageOption> LanguageOptions => [new("auto", T("ui.language.auto")), .. Core.Localization.Current.Languages];
    public string LanguageSelection
    {
        get => Core.Localization.Current.Selection;
        set { if (!string.IsNullOrEmpty(value)) Core.Localization.Current.SetLanguage(value); }
    }
    public ObservableCollection<ContextChoice> ContextChoices { get; } = [];
    public ModelDefinition? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (value is null || !CanSelectModel || !ApplyModelSelection(value)) return;
            SaveSettings();
            Refresh();
            _scan = ScanSelectedAsync(value);
        }
    }
    public ContextChoice? SelectedContext
    {
        get => _selectedContext;
        set
        {
            if (value is null || !_rebuildingChoices && !CanChangeContext || !Set(ref _selectedContext, value)) return;
            if (!_rebuildingChoices && SelectedModel is not null) { _settings.Contexts[SelectedModel.Id] = value.Value; SaveSettings(); }
            Refresh();
        }
    }
    public bool IsBusy => _busy || _scanning;
    public bool CanSelectModel => !_isDisposed && !_busy && !_scanning && _session.Status.State is ServerState.Stopped or ServerState.Faulted;
    public bool CanChangeContext => !_busy && ContextChoices.Count > 1;
    public bool CanOpenWebUi => _session.Status.State == ServerState.Running;
    public bool HasPendingContext => CanOpenWebUi && SelectedContext?.Value != _activeContext;
    public bool IsProgressVisible => IsBusy;
    public bool HasError => _error.Length > 0;
    public bool IsIndeterminate => _indeterminate;
    public double ProgressPercent => _progressPercent;
    public string ProgressMessage => _scanning && !_busy ? T("ui.status.scanning") : Core.Localization.Current.Retranslate(_progressMessage);
    public string StatusNote => Core.Localization.Current.Retranslate(_statusNote);
    public string MemoryUsageLabel => CanOpenWebUi ? T("ui.memory.current") : T("ui.memory.guide");
    public string MemoryUsage => CanOpenWebUi
        ? _memoryUsage is null ? T("ui.memory.reading") : _memoryUsage + (_privateRamBytes is long memory ? "\n" + T("ui.memory.private", (memory / 1_000_000d).ToString("0")) : "")
        : SelectedPresentation?.MemoryGuide ?? "";
    public string DownloadLabel => SelectedModel?.DownloadLabel ?? "";
    public bool NeedsRuntimePrerequisite => _prerequisiteDownload is not null;
    public string AccelerationLabel => Models.All(m => m.Profile.MtpDraftMax > 0) ? T("ui.acceleration.all") : T("ui.acceleration.mixed");
    public string ApiAddress => "http://127.0.0.1:8080/v1";
    public string ApiModelId => SelectedModel?.ApiModelId ?? "";
    public string ConnectionLabel => CanOpenWebUi ? T("ui.connection.ready") : T("ui.connection.waiting");
    public string ContextHint => HasPendingContext ? T("ui.context.running", _activeContext / 1024 ?? 0)
        : T("ui.context.hint");
    public string StatusLabel => _error.Length > 0 ? T("ui.status.attention") : CanOpenWebUi ? T("ui.status.running") : IsBusy ? T("ui.status.preparing") : T("ui.status.off");
    public string StatusTitle => _error.Length > 0 ? T("ui.status.failedTitle") : CanOpenWebUi ? T("ui.status.runningTitle")
        : _busy ? (_session.Status.State == ServerState.Stopping ? T("ui.status.stoppingTitle") : T("ui.status.preparingTitle"))
        : _scanning ? T("ui.status.scanningTitle") : _installed is null ? T("ui.status.firstTitle") : T("ui.status.readyTitle");
    public string StatusDescription => _error.Length > 0 ? Core.Localization.Current.Retranslate(_error) : CanOpenWebUi ? T("ui.status.runningDescription")
        : _busy ? ProgressMessage : _scanning ? T("ui.status.scanningDescription")
        : _installed is null ? T("ui.status.firstDescription") : T("ui.status.readyDescription");
    public string PrimaryLabel => _busy ? (_session.Status.State == ServerState.Stopping ? T("ui.action.stopping") : T("ui.action.cancel"))
        : CanOpenWebUi ? T("ui.action.stop") : _scanning ? T("ui.action.scanning") : _installed is null ? T("ui.action.download") : T("ui.action.start");
    public bool IsServerRunning => CanOpenWebUi;
    public ICommand PrimaryCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand CopyApiCommand { get; }
    public ICommand OpenWebUiCommand { get; }
    public ICommand ManageModelsCommand { get; }
    public ICommand SelectModelCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand ConnectModelFileCommand { get; }
    public ICommand ChooseModelFolderCommand { get; }
    public ICommand ImportCatalogCommand { get; }
    public ICommand OpenModelSourceCommand { get; }
    public ICommand InstallPrerequisiteCommand { get; }
    public event EventHandler? SessionChanged;

    public LauncherViewModel(ModelCatalog catalog, LauncherPaths paths, LauncherSettings settings, SettingsStore store,
        IInstallationService? installer = null, IServerSession? session = null)
    {
        _catalog = catalog; _paths = paths; _settings = settings; _store = store;
        Models = new(catalog.Models);
        ModelCards = new(catalog.Models.Select(model => new ModelPresentation(model)));
        Core.Localization.Current.LanguageChanged += OnLanguageChanged;
        _session = session ?? new ServerSession();
        _installer = installer ?? CreateInstaller();
        _session.StatusChanged += OnSessionStatus;
        PrimaryCommand = Command(_ => Primary(), _ => !_scanning && _session.Status.State != ServerState.Stopping && !_isDisposed);
        RestartCommand = Command(_ => BeginOperation(RestartAsync), _ => HasPendingContext && !_busy);
        CopyApiCommand = Command(_ => { try { System.Windows.Clipboard.SetText(ApiAddress); Note(T("ui.note.copied")); } catch (Exception ex) { Note(T("ui.note.copyFailed", ex.Message)); } });
        OpenWebUiCommand = Command(_ => OpenLink("http://127.0.0.1:8080/"), _ => CanOpenWebUi);
        ManageModelsCommand = Command(_ => { var window = new ModelManagerWindow(this) { Owner = System.Windows.Application.Current.MainWindow }; window.ShowDialog(); });
        SelectModelCommand = Command(value => { if (value is ModelDefinition model) SelectedModel = model; }, _ => CanSelectModel);
        OpenFolderCommand = Command(_ => OpenDirectory(_installed is null ? _paths.Models : Path.GetDirectoryName(_installed.ModelPath)!));
        OpenLogsCommand = Command(_ => OpenDirectory(_paths.Logs));
        ConnectModelFileCommand = Command(_ => ChooseModelFile(), _ => CanSelectModel);
        ChooseModelFolderCommand = Command(_ => ChooseModelFolder(), _ => CanSelectModel);
        ImportCatalogCommand = Command(_ => ImportCatalog(), _ => CanSelectModel);
        OpenModelSourceCommand = Command(_ => { if (SelectedModel is not null) OpenLink(SelectedModel.SourceUrl); });
        InstallPrerequisiteCommand = Command(_ => { if (_prerequisiteDownload is not null) OpenLink(_prerequisiteDownload.AbsoluteUri); }, _ => NeedsRuntimePrerequisite);
        LoadExtraCatalog();
        SelectedModel = Models.FirstOrDefault(m => m.Id == settings.SelectedModelId) ?? Models[0];
        _memoryTimer.Tick += async (_, _) => await ReadMemoryAsync();
        _memoryTimer.Start();
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => OnLanguageChanged(sender, args)); return; }
        if (_isDisposed) return;
        _settings.LanguagePreference = Core.Localization.Current.Selection;
        SaveSettings();
        foreach (var card in ModelCards) card.RefreshLanguage();
        foreach (var choice in ContextChoices) choice.RefreshLanguage();
        OnPropertyChanged(nameof(LanguageOptions));
        OnPropertyChanged(nameof(LanguageSelection));
        Refresh();
    }

    private IInstallationService CreateInstaller() => new InstallationService(_paths, searchDirectories: _settings.ModelFolders.ToArray(), modelFiles: _settings.ModelFiles);

    private bool ApplyModelSelection(ModelDefinition model)
    {
        if (!Set(ref _selectedModel, model, nameof(SelectedModel))) return false;
        _settings.SelectedModelId = model.Id;
        _installed = null;
        _error = "";
        _rebuildingChoices = true;
        try
        {
            ContextChoices.Clear();
            foreach (var size in model.Profile.ContextSizes)
                ContextChoices.Add(new(size, size == model.Profile.DefaultContext));
            var selected = _settings.Contexts.GetValueOrDefault(model.Id, model.Profile.DefaultContext);
            SelectedContext = ContextChoices.FirstOrDefault(c => c.Value == selected) ?? ContextChoices.First(c => c.Value == model.Profile.DefaultContext);
        }
        finally { _rebuildingChoices = false; }
        return true;
    }

    private RelayCommand Command(Action<object?> action, Predicate<object?>? predicate = null) { var command = new RelayCommand(action, predicate); _commands.Add(command); return command; }
    private void Note(string message) { _statusNote = message; OnPropertyChanged(nameof(StatusNote)); }
    private void Refresh()
    {
        foreach (var property in new[] { nameof(SelectedPresentation), nameof(IsBusy), nameof(CanSelectModel), nameof(CanChangeContext), nameof(CanOpenWebUi), nameof(HasPendingContext), nameof(IsProgressVisible), nameof(HasError), nameof(IsIndeterminate), nameof(ProgressPercent), nameof(ProgressMessage), nameof(StatusNote), nameof(MemoryUsage), nameof(MemoryUsageLabel), nameof(DownloadLabel), nameof(ApiModelId), nameof(ConnectionLabel), nameof(ContextHint), nameof(StatusLabel), nameof(StatusTitle), nameof(StatusDescription), nameof(PrimaryLabel), nameof(IsServerRunning) }) OnPropertyChanged(property);
        foreach (var command in _commands) command.Refresh();
        OnPropertyChanged(nameof(NeedsRuntimePrerequisite));
        OnPropertyChanged(nameof(AccelerationLabel));
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ScanSelectedAsync(ModelDefinition model)
    {
        _scanCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        _scanning = true; _indeterminate = true; Refresh();
        try
        {
            var found = await _installer.FindAsync(model, cancellation.Token);
            if (!cancellation.IsCancellationRequested && SelectedModel?.Id == model.Id) _installed = found;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cancellation.IsCancellationRequested && SelectedModel?.Id == model.Id) Note(T("ui.note.scanFailed", ex.Message)); }
        finally
        {
            if (ReferenceEquals(cancellation, _scanCancellation)) { _scanning = false; _scanCancellation = null; Refresh(); }
            cancellation.Dispose();
        }
    }

    private void Primary()
    {
        if (_busy) { _operationCancellation?.Cancel(); _progressMessage = T("ui.status.cancelling"); Refresh(); return; }
        BeginOperation(CanOpenWebUi ? StopAsync : StartAsync);
    }

    private void BeginOperation(Func<CancellationToken, Task> action, bool stopOnFailure = true)
    {
        if (_busy || _isDisposed) return;
        _busy = true; _error = ""; _statusNote = ""; _indeterminate = true; _prerequisiteDownload = null;
        _operationCancellation = new();
        Refresh();
        _operation = RunOperationAsync(action, _operationCancellation, stopOnFailure);
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> action, CancellationTokenSource cancellation, bool stopOnFailure)
    {
        try { await action(cancellation.Token); }
        catch (MissingRuntimePrerequisiteException ex) { _error = ex.Message; _prerequisiteDownload = ex.DownloadUri; }
        catch (OperationCanceledException) { if (stopOnFailure) await StopAfterFailureAsync(); Note(T("ui.note.cancelled")); }
        catch (Exception ex) { if (stopOnFailure) await StopAfterFailureAsync(); _error = ex.Message; SaveError(ex); }
        finally { _busy = false; _indeterminate = true; _operationCancellation = null; cancellation.Dispose(); Refresh(); }
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        RuntimePrerequisites.EnsureAvailable();
        var model = SelectedModel ?? throw new InvalidOperationException(T("ui.error.selectModel"));
        var context = SelectedContext?.Value ?? model.Profile.DefaultContext;
        var progress = new Progress<InstallationProgress>(value =>
        {
            _progressMessage = value.Message; _progressPercent = value.Percent ?? 0; _indeterminate = value.Percent is null; Refresh();
        });
        var runtime = await _installer.EnsureRuntimeAsync(_catalog.Runtime, progress, cancellationToken);
        _installed = await _installer.EnsureModelAsync(model, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _progressMessage = T("ui.status.loading"); _indeterminate = true; Refresh();
        await _session.StartAsync(new(_installed, runtime, context, _paths.Logs), cancellationToken);
        _activeContext = context;
    }

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        _progressMessage = T("ui.status.stoppingDescription"); Refresh();
        await _session.StopAsync(cancellationToken); _activeContext = null;
    }
    private async Task RestartAsync(CancellationToken cancellationToken) { await StopAsync(cancellationToken); cancellationToken.ThrowIfCancellationRequested(); await StartAsync(cancellationToken); }
    private async Task StopAfterFailureAsync() { try { await _session.StopAsync(); } catch (Exception ex) { SaveError(ex); } }

    private void OnSessionStatus(object? sender, SessionStatus status)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => OnSessionStatus(sender, status)); return; }
        if (_isDisposed) return;
        if (status.State == ServerState.Faulted) _error = status.Message;
        if (status.State is ServerState.Starting or ServerState.Stopping) _progressMessage = status.Message;
        if (status.State != ServerState.Running) _memoryUsage = null;
        Refresh();
        if (status.State == ServerState.Running) _ = ReadMemoryAsync();
    }
    private async Task ReadMemoryAsync()
    {
        if (_readingMemory || _isDisposed || !CanOpenWebUi || _session.Status.ProcessId is not int pid) return;
        _readingMemory = true;
        try
        {
            var sample = await Task.Run(() => { using var metrics = new ProcessMetrics(pid); return metrics.Read(); });
            if (!_isDisposed && CanOpenWebUi && _session.Status.ProcessId == pid)
            {
                _memoryUsage = $"RAM {sample.WorkingSetBytes / 1_000_000_000d:0.0} GB";
                if (sample.DedicatedGpuBytes is long gpu) _memoryUsage += $" · VRAM {gpu / 1_000_000_000d:0.0} GB";
                _privateRamBytes = sample.PrivateWorkingSetBytes;
                OnPropertyChanged(nameof(MemoryUsage));
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { /* Process exit or unavailable telemetry must not interrupt the server. */ }
        finally { _readingMemory = false; }
    }
    private void SaveSettings()
    {
        if (_deferSettingsSave) { _settingsSavePending = true; return; }
        try { _store.Save(_settings); }
        catch (Exception ex) { Note(T("ui.note.saveFailed", ex.Message)); }
    }
    private void SaveError(Exception exception)
    {
        try { Directory.CreateDirectory(_paths.Logs); File.AppendAllText(Path.Combine(_paths.Logs, "launcher.log"), $"{DateTimeOffset.Now:O} {exception}\n"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private void OpenLink(string address)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception ex) { Note(T("ui.note.browserFailed", ex.Message)); }
    }
    private void OpenDirectory(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true }); }
        catch (Exception ex) { Note(T("ui.note.folderFailed", ex.Message)); }
    }
    private void ChooseModelFile()
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("ui.picker.gguf"), Filter = T("ui.picker.ggufFilter"),
            DefaultExt = ".gguf", CheckFileExists = true, Multiselect = false
        };
        if (picker.ShowDialog() != true || !CanSelectModel) return;
        _ = ConnectModelFileAsync(picker.FileName);
    }

    public Task ConnectModelFileAsync(string path)
    {
        if (!CanSelectModel) return Task.CompletedTask;
        BeginOperation(token => ConnectModelFileCoreAsync(path, token), stopOnFailure: false);
        return _operation ?? Task.CompletedTask;
    }

    private async Task ConnectModelFileCoreAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Path.GetExtension(fullPath).Equals(".gguf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(T("ui.error.ggufNotRecognized"));
        _progressMessage = T("engine.install.verifying", Path.GetFileName(fullPath));
        Refresh();
        var progress = new Progress<InstallationProgress>(value =>
        {
            if (_isDisposed || cancellationToken.IsCancellationRequested || _operationCancellation?.Token != cancellationToken) return;
            _progressMessage = value.Message; _progressPercent = value.Percent ?? 0; _indeterminate = value.Percent is null; Refresh();
        });
        var model = await _installer.MatchModelFileAsync(fullPath, Models.ToArray(), progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (model is null) throw new InvalidDataException(T("ui.error.ggufNotRecognized"));
        var main = model.Artifacts.Single(artifact => artifact.Role == "model");
        var hadPreviousPath = _settings.ModelFiles.TryGetValue(main.Sha256, out var previousPath);
        var previousModelId = _settings.SelectedModelId;
        _settings.ModelFiles[main.Sha256] = fullPath;
        _deferSettingsSave = true;
        InstalledModel? installed;
        try
        {
            // Resolve the companion again even when this model is already selected.
            // Keep the visible selection unchanged until verification and persistence finish.
            installed = await _installer.FindAsync(model, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _settings.SelectedModelId = model.Id;
            _store.Save(_settings);
        }
        catch
        {
            if (hadPreviousPath) _settings.ModelFiles[main.Sha256] = previousPath!;
            else _settings.ModelFiles.Remove(main.Sha256);
            _settings.SelectedModelId = previousModelId;
            throw;
        }
        finally
        {
            _deferSettingsSave = false;
            if (_settingsSavePending) { _settingsSavePending = false; SaveSettings(); }
        }
        ApplyModelSelection(model);
        _installed = installed;
        Note(T("ui.note.ggufConnected", model.DisplayName));
        Refresh();
    }

    private void ChooseModelFolder()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = T("ui.picker.modelFolder") };
        if (picker.ShowDialog() != true || !CanSelectModel) return;
        if (!_settings.ModelFolders.Contains(picker.FolderName, StringComparer.OrdinalIgnoreCase)) _settings.ModelFolders.Add(picker.FolderName);
        SaveSettings();
        _scan = ReplaceInstallerAndScanAsync();
        Note(T("ui.note.folderConnected"));
    }
    private async Task ReplaceInstallerAndScanAsync()
    {
        _scanCancellation?.Cancel();
        // Wait for the previous reader before disposing the installer's file/network handles.
        var previous = _scan;
        if (previous is not null) await previous;
        if (_isDisposed) return;
        (_installer as IDisposable)?.Dispose();
        _installer = CreateInstaller();
        if (SelectedModel is not null) await ScanSelectedAsync(SelectedModel);
    }
    private void LoadExtraCatalog()
    {
        var path = Path.Combine(_paths.Root, "catalog.user.json");
        if (!File.Exists(path)) return;
        try { MergeCatalog(CatalogLoader.Load(path)); }
        catch (Exception ex) { Note(T("ui.note.catalogReadFailed", ex.Message)); }
    }
    private void MergeCatalog(ModelCatalog extra)
    {
        if (extra.Runtime.Version != _catalog.Runtime.Version || !extra.Runtime.Sha256.Equals(_catalog.Runtime.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(T("ui.error.catalogRuntime"));
        foreach (var model in extra.Models)
        {
            if (Models.Any(m => m.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException(T("ui.error.duplicateModel", model.Id));
        }
        foreach (var model in extra.Models) { Models.Add(model); ModelCards.Add(new(model)); }
    }
    private void ImportCatalog()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = T("ui.picker.catalog"), Filter = T("ui.picker.catalogFilter") };
        if (picker.ShowDialog() != true || !CanSelectModel) return;
        try
        {
            var extra = CatalogLoader.Load(picker.FileName);
            // Validate all records before changing either the visible catalog or the persistent file.
            if (extra.Runtime.Version != _catalog.Runtime.Version || !extra.Runtime.Sha256.Equals(_catalog.Runtime.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(T("ui.error.catalogRuntime"));
            if (extra.Models.Any(m => Models.Any(existing => existing.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase)))) throw new InvalidDataException(T("ui.error.catalogDuplicates"));
            var allExtras = Models.Where(m => !_catalog.Models.Any(b => b.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase))).Concat(extra.Models).ToArray();
            var saved = extra with { Models = allExtras };
            CatalogLoader.Validate(saved);
            Directory.CreateDirectory(_paths.Root);
            var target = Path.Combine(_paths.Root, "catalog.user.json");
            File.WriteAllText(target + ".tmp", System.Text.Json.JsonSerializer.Serialize(saved, CatalogLoader.JsonOptions));
            File.Move(target + ".tmp", target, true);
            MergeCatalog(extra); Refresh(); Note(T("ui.note.catalogAdded"));
        }
        catch (Exception ex) { Note(T("ui.note.catalogAddFailed", ex.Message)); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _memoryTimer.Stop();
        Core.Localization.Current.LanguageChanged -= OnLanguageChanged;
        _scanCancellation?.Cancel(); _operationCancellation?.Cancel();
        if (_operation is not null) await _operation;
        if (_scan is not null) await _scan;
        _session.StatusChanged -= OnSessionStatus;
        await _session.DisposeAsync();
        (_installer as IDisposable)?.Dispose();
    }
}
