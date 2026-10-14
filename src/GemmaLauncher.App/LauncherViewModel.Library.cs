using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using GemmaLauncher.Core;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

public sealed partial class LauncherViewModel
{
    private int _pageIndex;
    private int _modelPageIndex;
    private PresetChoice? _selectedPreset;
    private string _presetName = "";
    private string _presetPrompt = "";
    private string _presetTemperature = "";
    private string _presetTopP = "";
    private string _presetMaxTokens = "";
    private string _hfAddress = "";
    private HuggingFaceFile? _selectedHfFile;
    private readonly HuggingFaceModels _huggingFace = new();
    public int PageIndex { get => _pageIndex; set { if (Set(ref _pageIndex, value)) RefreshPageProperties(); } }
    public int ModelPageIndex { get => _modelPageIndex; set => Set(ref _modelPageIndex, value); }
    public ObservableCollection<ModelFamily> Families { get; } = [];
    public ObservableCollection<PresetChoice> Presets { get; } = [];
    public ObservableCollection<GlossaryRow> Glossary { get; } = [];
    public ObservableCollection<HuggingFaceFile> HuggingFaceFiles { get; } = [];
    public ObservableCollection<ModelPresentation> LibraryModels { get; } = [];
    public bool HasLibraryModels => LibraryModels.Count > 0;
    public bool HasSelectedModel => SelectedModel is not null;
    public bool ShowRunEmpty => !HasSelectedModel;
    public bool ShowLibraryEmpty => !HasLibraryModels;
    public bool ShowOperation => PageIndex == 0 && HasSelectedModel || IsBusy || IsServerRunning || HasError;
    public bool ShowPrimaryAction => PageIndex == 0 && HasSelectedModel || _busy || IsServerRunning;
    public bool ShowOperationModel => HasSelectedModel && ((!_busy && !HasError) || _operationUsesModel);
    private void RefreshPageProperties()
    {
        foreach (var property in new[] { nameof(HasSelectedModel), nameof(ShowRunEmpty), nameof(ShowLibraryEmpty), nameof(ShowOperation), nameof(ShowPrimaryAction), nameof(ShowOperationModel) }) OnPropertyChanged(property);
    }
    public bool HasHuggingFaceFiles => HuggingFaceFiles.Count > 0;
    public string HuggingFaceAddress { get => _hfAddress; set { if (Set(ref _hfAddress, value)) { HuggingFaceFiles.Clear(); SelectedHuggingFaceFile = null; OnPropertyChanged(nameof(HasHuggingFaceFiles)); } } }
    public HuggingFaceFile? SelectedHuggingFaceFile { get => _selectedHfFile; set { Set(ref _selectedHfFile, value); AddHuggingFaceCommand?.Refresh(); } }
    public RelayCommand AddHuggingFaceCommand { get; private set; } = null!;
    public ICommand FindHuggingFaceCommand { get; private set; } = null!;
    public ICommand UseLibraryModelCommand { get; private set; } = null!;
    public ICommand AddModelCommand { get; private set; } = null!;
    public ICommand NewPresetCommand { get; private set; } = null!;
    public ICommand SavePresetCommand { get; private set; } = null!;
    public ICommand DeletePresetCommand { get; private set; } = null!;
    public ICommand ImportGlossaryCommand { get; private set; } = null!;
    public ICommand ConnectMtpCommand { get; private set; } = null!;
    public ICommand ResetLoadOptionsCommand { get; private set; } = null!;
    public ICommand RemoveLibraryModelCommand { get; private set; } = null!;
    public bool CanEditPreset => SelectedPreset is { Id: not "none" } && !IsBusy;
    public bool CanChangePreset => !IsBusy;
    public PresetChoice? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (value is null || ReferenceEquals(_selectedPreset, value) || !SavePendingPreset()) return;
            Set(ref _selectedPreset, value);
            if (SelectedModel is not null) _settings.ModelPresets[SelectedModel.Id] = value.Id;
            LoadPresetEditor();
            if (_session is ServerSession session) session.UpdatePreset(value.Preset);
            SaveSettings(); Refresh();
        }
    }
    public string PresetName { get => _presetName; set => Set(ref _presetName, value); }
    public string PresetPrompt { get => _presetPrompt; set => Set(ref _presetPrompt, value); }
    public string PresetTemperature { get => _presetTemperature; set => Set(ref _presetTemperature, value); }
    public string PresetTopP { get => _presetTopP; set => Set(ref _presetTopP, value); }
    public string PresetMaxTokens { get => _presetMaxTokens; set => Set(ref _presetMaxTokens, value); }
    public string[] GpuLayerChoices { get; } = ["auto", "all", "0", "8", "16", "24", "32", "48", "64"];
    public string[] CacheChoices { get; } = ["f16", "q8_0", "q4_0"];
    public string[] ToggleChoices { get; } = ["auto", "on", "off"];
    private ModelLoadOptions LoadOptions => SelectedModel is null ? new() : _settings.LoadOptions.GetValueOrDefault(SelectedModel.Id) ?? ModelLoadOptions.For(SelectedModel);
    public string GpuLayers { get => LoadOptions.GpuLayers; set => ChangeOptions(LoadOptions with { GpuLayers = value }); }
    public string CacheType { get => LoadOptions.CacheType; set => ChangeOptions(LoadOptions with { CacheType = value, FlashAttention = value != "f16" ? "on" : LoadOptions.FlashAttention }); }
    public string FlashAttention { get => LoadOptions.FlashAttention; set => ChangeOptions(LoadOptions with { FlashAttention = value }); }
    public string Reasoning { get => LoadOptions.Reasoning; set => ChangeOptions(LoadOptions with { Reasoning = value }); }
    public bool UseMtp { get => LoadOptions.UseMtp; set => ChangeOptions(LoadOptions with { UseMtp = value }); }
    public int DraftTokens { get => LoadOptions.DraftTokens; set => ChangeOptions(LoadOptions with { DraftTokens = value }); }
    public string ChatTemplate { get => LoadOptions.ChatTemplate; set => ChangeOptions(LoadOptions with { ChatTemplate = value }); }
    public bool HasMtp => SelectedModel is not null && (SelectedModel.Artifacts.Any(a => a.Role == "mtp") || _settings.MtpFiles.ContainsKey(SelectedModel.Id));
    public string MtpLabel => SelectedModel is not null && _settings.MtpFiles.TryGetValue(SelectedModel.Id, out var artifact) ? artifact.Filename : HasMtp ? T("ui.library.bundledMtp") : T("ui.library.noMtp");
    public string TemplateHint => SelectedModel?.HasChatTemplate != false ? T("ui.library.templateAuto") : T("ui.library.templateMissing");

    private void InitializeLibrary()
    {
        foreach (var model in _settings.UserModels.ToArray())
        {
            try
            {
                CatalogLoader.Validate(_catalog with { Models = [model] });
                if (!Models.Any(existing => existing.Id == model.Id)) { Models.Add(model); ModelCards.Add(new(model)); }
            }
            catch (Exception ex) { Note(T("ui.note.catalogReadFailed", ex.Message)); }
        }
        foreach (var group in ModelCards.Where(card => _catalog.Models.Any(model => model.Id == card.Definition.Id)).GroupBy(card => card.Definition.FamilyId.Length > 0 ? card.Definition.FamilyId : card.Definition.Id).OrderBy(group => group.First().Definition.FamilyOrder)) Families.Add(new(group));
        foreach (var model in Models)
        {
            var main = model.Artifacts.Single(a => a.Role == "model");
            if (_settings.ModelFiles.ContainsKey(main.Sha256) || File.Exists(Path.Combine(_paths.Models, model.Id, main.Sha256[..12], main.Filename))) RememberModel(model);
        }
        // Older versions saved a selection before the library existed. Migrate it once into the same registry.
        foreach (var model in Models.Where(model => model.Id == _settings.SelectedModelId || _settings.UserModels.Any(user => user.Id == model.Id))) RememberModel(model);
        SyncLibrary();
        foreach (var card in ModelCards) ResetFileState(card);
        if (!_settings.PresetsInitialized)
        {
            var language = Core.Localization.Current.Languages.First(option => option.Code == Core.Localization.Current.LanguageCode).Name;
            _settings.Presets.Add(new() { Id = "translation", Name = T("ui.library.translationPreset"), Prompt = T("ui.library.translationPrompt", language) });
            _settings.Presets.Add(new() { Id = "creative", Name = T("ui.library.creativePreset"), Prompt = T("ui.library.creativePrompt") });
            _settings.PresetsInitialized = true;
        }
        Presets.Add(new(TaskPreset.Empty));
        foreach (var preset in _settings.Presets) Presets.Add(new(preset));
        UseLibraryModelCommand = Command(value =>
        {
            var model = value is ModelPresentation card ? card.Definition : value as ModelDefinition;
            if (model is not null && TrySelectModel(model)) PageIndex = 0;
        }, _ => CanSelectModel);
        AddModelCommand = Command(_ => { PageIndex = 1; ModelPageIndex = 2; });
        FindHuggingFaceCommand = Command(_ => BeginOperation(FindHuggingFaceAsync, false), _ => CanSelectModel);
        AddHuggingFaceCommand = Command(_ => { if (SavePendingPreset()) BeginOperation(AddHuggingFaceAsync, false); }, _ => CanSelectModel && SelectedHuggingFaceFile is not null);
        NewPresetCommand = Command(_ =>
        {
            if (!SavePendingPreset()) return;
            var preset = new TaskPreset { Name = T("ui.library.newPreset") };
            _settings.Presets.Add(preset); var choice = new PresetChoice(preset); Presets.Add(choice); SelectedPreset = choice;
        }, _ => !IsBusy);
        SavePresetCommand = Command(_ => SavePreset(), _ => CanEditPreset);
        DeletePresetCommand = Command(_ =>
        {
            if (SelectedPreset is not { Id: not "none" } selected) return;
            _settings.Presets.RemoveAll(preset => preset.Id == selected.Id);
            foreach (var model in _settings.ModelPresets.Where(pair => pair.Value == selected.Id).Select(pair => pair.Key).ToArray()) _settings.ModelPresets.Remove(model);
            _selectedPreset = null; SelectedPreset = Presets[0]; Presets.Remove(selected); SaveSettings();
        }, _ => CanEditPreset);
        ImportGlossaryCommand = Command(_ => ImportGlossary(), _ => CanEditPreset);
        ConnectMtpCommand = Command(_ => ChooseMtp(), _ => CanSelectModel && HasSelectedModel);
        ResetLoadOptionsCommand = Command(_ =>
        {
            if (SelectedModel is null) return;
            _settings.LoadOptions.Remove(SelectedModel.Id); _settings.MtpFiles.Remove(SelectedModel.Id);
            SaveSettings(); RefreshLibraryProperties(); _scan = ScanSelectedAsync(SelectedModel);
        }, _ => CanSelectModel && HasSelectedModel);
        RemoveLibraryModelCommand = Command(value => RemoveModel(value as ModelPresentation), value => CanSelectModel && value is ModelPresentation { IsUserModel: true });
    }

    private void RememberModel(ModelDefinition model) { if (!_settings.LibraryModelIds.Contains(model.Id)) _settings.LibraryModelIds.Add(model.Id); }

    private void SyncLibrary()
    {
        var registered = ModelCards.Where(card => _settings.LibraryModelIds.Contains(card.Definition.Id)).ToArray();
        foreach (var card in LibraryModels.Except(registered).ToArray()) LibraryModels.Remove(card);
        foreach (var card in registered) if (!LibraryModels.Contains(card)) LibraryModels.Add(card);
        OnPropertyChanged(nameof(HasLibraryModels));
        RefreshPageProperties();
    }

    private bool TrySelectModel(ModelDefinition model)
    {
        if (!CanSelectModel || !Models.Contains(model) || !SavePendingPreset()) return false;
        var previousId = _settings.SelectedModelId;
        var wasRegistered = _settings.LibraryModelIds.Contains(model.Id);
        _settings.SelectedModelId = model.Id; RememberModel(model);
        try { _store.Save(_settings); }
        catch (Exception ex)
        {
            _settings.SelectedModelId = previousId;
            if (!wasRegistered) _settings.LibraryModelIds.Remove(model.Id);
            Note(T("ui.note.saveFailed", ex.Message));
            // Restore the bound selector if WPF already displayed the rejected candidate.
            OnPropertyChanged(nameof(SelectedModel));
            return false;
        }
        _error = ""; _statusNote = ""; _prerequisiteDownload = null;
        SyncLibrary();
        ApplyModelSelection(model);
        Refresh(); _scan = ScanSelectedAsync(model);
        return true;
    }

    private ModelFileState UnavailableFileState(ModelDefinition model)
    {
        var local = model.Artifacts.Where(artifact => artifact.IsLocal).ToArray();
        return local.Length == model.Artifacts.Length || local.Any(artifact => !File.Exists(_settings.ModelFiles.GetValueOrDefault(artifact.Sha256)))
            ? ModelFileState.ReconnectRequired : ModelFileState.DownloadNeeded;
    }

    private bool NeedsMtpReconnect => SelectedModel is not null && EffectiveModel(SelectedModel).Artifacts.Any(artifact =>
        artifact.Role == "mtp" && artifact.IsLocal && !File.Exists(_settings.ModelFiles.GetValueOrDefault(artifact.Sha256))) &&
        !EffectiveModel(SelectedModel).Artifacts.Any(artifact => artifact.Role == "model" && artifact.IsLocal && !File.Exists(_settings.ModelFiles.GetValueOrDefault(artifact.Sha256)));

    private void ResetFileState(ModelPresentation card)
    {
        var model = EffectiveModel(card.Definition);
        var state = UnavailableFileState(model);
        var hasCandidate = model.Artifacts.Any(artifact => File.Exists(_settings.ModelFiles.GetValueOrDefault(artifact.Sha256)) || File.Exists(Path.Combine(_paths.Models, model.Id, artifact.Sha256[..12], artifact.Filename)));
        card.FileState = hasCandidate || _settings.ModelFolders.Count > 0 ? ModelFileState.Unchecked : state;
    }
    private void OnLibraryModelSelected()
    {
        if (SelectedModel is null || Presets.Count == 0) return;
        var id = _settings.ModelPresets.GetValueOrDefault(SelectedModel.Id, "none");
        _selectedPreset = Presets.FirstOrDefault(preset => preset.Id == id) ?? Presets[0];
        OnPropertyChanged(nameof(SelectedPreset)); LoadPresetEditor(); RefreshLibraryProperties();
    }

    private void RefreshLibraryProperties()
    {
        SyncLibrary();
        foreach (var property in new[] { nameof(LibraryModels), nameof(HasLibraryModels), nameof(ShowOperation), nameof(CanEditPreset), nameof(CanChangePreset), nameof(GpuLayers), nameof(CacheType), nameof(FlashAttention), nameof(Reasoning), nameof(UseMtp), nameof(DraftTokens), nameof(ChatTemplate), nameof(HasMtp), nameof(MtpLabel), nameof(TemplateHint) }) OnPropertyChanged(property);
    }

    private void ChangeOptions(ModelLoadOptions options)
    {
        if (SelectedModel is null || !CanSelectModel) return;
        try
        {
            options.Validate();
            var changedMtp = options.UseMtp != LoadOptions.UseMtp;
            _settings.LoadOptions[SelectedModel.Id] = options; SaveSettings();
            if (changedMtp) _scan = ScanSelectedAsync(SelectedModel);
        }
        catch (InvalidDataException ex) { Note(ex.Message); }
        RefreshLibraryProperties();
    }

    private ModelDefinition EffectiveModel(ModelDefinition model)
    {
        var options = _settings.LoadOptions.GetValueOrDefault(model.Id) ?? ModelLoadOptions.For(model);
        var mtp = _settings.MtpFiles.GetValueOrDefault(model.Id) ?? model.Artifacts.FirstOrDefault(a => a.Role == "mtp");
        var desiredMtp = options.UseMtp ? mtp : null;
        if (ReferenceEquals(desiredMtp, model.Artifacts.FirstOrDefault(a => a.Role == "mtp")) && model.Profile.MtpDraftMax == (desiredMtp is null ? 0 : options.DraftTokens)) return model;
        return model with { Artifacts = options.UseMtp && mtp is not null ? [model.Artifacts.Single(a => a.Role == "model"), mtp] : [model.Artifacts.Single(a => a.Role == "model")],
            Profile = model.Profile with { MtpDraftMax = options.UseMtp && mtp is not null ? options.DraftTokens : 0 } };
    }

    private void LoadPresetEditor()
    {
        var preset = SelectedPreset?.Preset ?? TaskPreset.Empty;
        PresetName = preset.Id == "none" ? T("ui.library.defaults") : preset.Name; PresetPrompt = preset.Prompt;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        PresetTemperature = preset.Temperature?.ToString(culture) ?? ""; PresetTopP = preset.TopP?.ToString(culture) ?? ""; PresetMaxTokens = preset.MaxTokens?.ToString(culture) ?? "";
        Glossary.Clear(); foreach (var entry in preset.Glossary) Glossary.Add(new() { Source = entry.Source, Target = entry.Target });
    }

    private bool SavePendingPreset()
    {
        if (SelectedPreset is not { Id: not "none" } selected) return true;
        var preset = selected.Preset;
        var rows = Glossary.Where(row => row.Source.Length > 0 || row.Target.Length > 0).Select(row => new GlossaryEntry(row.Source.Trim(), row.Target.Trim()));
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var dirty = PresetName != preset.Name || PresetPrompt != preset.Prompt || !rows.SequenceEqual(preset.Glossary) ||
            PresetTemperature != (preset.Temperature?.ToString(culture) ?? "") || PresetTopP != (preset.TopP?.ToString(culture) ?? "") || PresetMaxTokens != (preset.MaxTokens?.ToString(culture) ?? "");
        return !dirty || SavePreset();
    }

    private bool SavePreset()
    {
        if (SelectedPreset is not { Id: not "none" } selected) return true;
        try
        {
            var updated = selected.Preset with { Name = PresetName.Trim(), Prompt = PresetPrompt,
                Glossary = Glossary.Where(row => row.Source.Length > 0 || row.Target.Length > 0).Select(row => new GlossaryEntry(row.Source.Trim(), row.Target.Trim())).ToArray(),
                Temperature = Number(PresetTemperature), TopP = Number(PresetTopP), MaxTokens = string.IsNullOrWhiteSpace(PresetMaxTokens) ? null : int.Parse(PresetMaxTokens, System.Globalization.CultureInfo.InvariantCulture) };
            updated.Validate();
            var index = _settings.Presets.FindIndex(preset => preset.Id == updated.Id);
            var previous = _settings.Presets[index]; _settings.Presets[index] = updated;
            try { _store.Save(_settings); } catch { _settings.Presets[index] = previous; throw; }
            selected.Preset = updated; selected.RefreshLanguage();
            if (_session is ServerSession session) session.UpdatePreset(updated);
            Note(T("ui.library.presetSaved"));
            return true;
        }
        catch (Exception ex) { Note(ex is FormatException or OverflowException ? T("engine.library.invalidPreset") : ex.Message); return false; }
        static double? Number(string value) => string.IsNullOrWhiteSpace(value) ? null : double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private void ImportGlossary()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "CSV / TSV (*.csv;*.tsv)|*.csv;*.tsv", CheckFileExists = true };
        if (picker.ShowDialog() != true) return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 4_000_000) throw new InvalidDataException(T("engine.library.invalidPreset"));
            var entries = TaskPreset.ReadGlossary(File.ReadAllText(picker.FileName));
            if (entries.Length + Glossary.Count > 2000) throw new InvalidDataException(T("engine.library.invalidPreset"));
            foreach (var entry in entries) Glossary.Add(new() { Source = entry.Source, Target = entry.Target });
        }
        catch (Exception ex) { Note(ex.Message); }
    }

    private async Task FindHuggingFaceAsync(CancellationToken token)
    {
        _progressMessage = T("ui.library.finding"); Refresh();
        HuggingFaceFiles.Clear(); SelectedHuggingFaceFile = null;
        var files = await _huggingFace.ListAsync(HuggingFaceAddress, token);
        foreach (var file in files) HuggingFaceFiles.Add(file);
        SelectedHuggingFaceFile = HuggingFaceFiles.FirstOrDefault(file => file.Filename.Contains("Q4_K_M", StringComparison.OrdinalIgnoreCase)) ?? HuggingFaceFiles.First();
        OnPropertyChanged(nameof(HasHuggingFaceFiles));
    }

    private async Task AddHuggingFaceAsync(CancellationToken token)
    {
        var file = SelectedHuggingFaceFile ?? throw new InvalidOperationException(T("ui.error.selectModel"));
        _progressMessage = T("ui.library.inspecting"); Refresh();
        var definition = await _huggingFace.InspectAsync(file, token);
        token.ThrowIfCancellationRequested();
        var existing = Models.FirstOrDefault(model => model.Artifacts.Any(artifact => artifact.Role == "model" && artifact.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)));
        definition = existing ?? definition;
        PersistRegistration(definition, new());
        ApplyModelSelection(definition); await ScanSelectedAsync(definition); PageIndex = 0; SaveSettings();
        Note(T("ui.library.added"));
    }

    private void PersistRegistration(ModelDefinition model, Dictionary<string, string> files)
    {
        CatalogLoader.Validate(_catalog with { Models = [model] });
        var oldModels = _settings.UserModels.ToList(); var oldFiles = new Dictionary<string, string>(_settings.ModelFiles); var oldLibrary = _settings.LibraryModelIds.ToList(); var oldSelectedId = _settings.SelectedModelId;
        try
        {
            if (!Models.Any(existing => existing.Id == model.Id)) _settings.UserModels.Add(model);
            foreach (var (hash, path) in files) _settings.ModelFiles[hash] = path;
            RememberModel(model); _settings.SelectedModelId = model.Id; _store.Save(_settings);
        }
        catch { _settings.UserModels = oldModels; _settings.ModelFiles.Clear(); foreach (var pair in oldFiles) _settings.ModelFiles[pair.Key] = pair.Value; _settings.LibraryModelIds = oldLibrary; _settings.SelectedModelId = oldSelectedId; throw; }
        if (!Models.Any(existing => existing.Id == model.Id)) { Models.Add(model); ModelCards.Add(new(model)); }
        RefreshLibraryProperties();
    }

    private void RemoveModel(ModelPresentation? card)
    {
        if (card is not { IsUserModel: true } || !SavePendingPreset()) return;
        var model = card.Definition;
        var next = LibraryModels.FirstOrDefault(item => item.Definition.Id != model.Id);
        var updated = _settings with
        {
            UserModels = _settings.UserModels.Where(item => item.Id != model.Id).ToList(),
            LibraryModelIds = _settings.LibraryModelIds.Where(id => id != model.Id).ToList(),
            LoadOptions = _settings.LoadOptions.Where(pair => pair.Key != model.Id).ToDictionary(),
            ModelPresets = _settings.ModelPresets.Where(pair => pair.Key != model.Id).ToDictionary(),
            MtpFiles = _settings.MtpFiles.Where(pair => pair.Key != model.Id).ToDictionary(),
            SelectedModelId = SelectedModel?.Id == model.Id ? next?.Definition.Id : _settings.SelectedModelId
        };
        try { _store.Save(updated); }
        catch (Exception ex) { Note(T("ui.note.saveFailed", ex.Message)); return; }
        _settings.UserModels = updated.UserModels; _settings.LibraryModelIds = updated.LibraryModelIds;
        _settings.LoadOptions = updated.LoadOptions; _settings.ModelPresets = updated.ModelPresets; _settings.MtpFiles = updated.MtpFiles;
        _settings.SelectedModelId = updated.SelectedModelId;
        ModelCards.Remove(card); Models.Remove(model);
        if (SelectedModel?.Id == model.Id)
        {
            if (next is not null) { ApplyModelSelection(next.Definition); _scan = ScanSelectedAsync(next.Definition); }
            else
            {
                _selectedModel = null; _installed = null; _selectedContext = null; _error = "";
                ContextChoices.Clear(); _selectedPreset = Presets.FirstOrDefault(); LoadPresetEditor();
                OnPropertyChanged(nameof(SelectedModel)); OnPropertyChanged(nameof(SelectedContext)); OnPropertyChanged(nameof(SelectedPreset));
                PageIndex = 1;
            }
        }
        Refresh();
    }

    private void ChooseMtp()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = T("ui.library.connectMtp"), Filter = T("ui.picker.ggufFilter"), CheckFileExists = true };
        if (picker.ShowDialog() != true || !CanSelectModel) return;
        BeginOperation(async token =>
        {
            var model = SelectedModel!;
            var mainOnly = model with { Artifacts = [model.Artifacts.Single(a => a.Role == "model")], Profile = model.Profile with { MtpDraftMax = 0 } };
            var installed = await _installer.FindAsync(mainOnly, token) ?? throw new FileNotFoundException(T("engine.library.mainFirst"));
            _progressMessage = T("ui.library.inspecting"); Refresh();
            var artifact = await ModelImportService.ImportMtpAsync(installed.ModelPath, picker.FileName, token);
            var previous = _settings.MtpFiles.GetValueOrDefault(model.Id);
            var hadPath = _settings.ModelFiles.TryGetValue(artifact.Sha256, out var oldPath);
            _settings.MtpFiles[model.Id] = artifact; _settings.ModelFiles[artifact.Sha256] = Path.GetFullPath(picker.FileName);
            try { _store.Save(_settings); }
            catch { if (previous is null) _settings.MtpFiles.Remove(model.Id); else _settings.MtpFiles[model.Id] = previous; if (hadPath) _settings.ModelFiles[artifact.Sha256] = oldPath!; else _settings.ModelFiles.Remove(artifact.Sha256); throw; }
            await ScanSelectedAsync(model); Note(T("ui.library.added"));
        }, false);
    }
}
