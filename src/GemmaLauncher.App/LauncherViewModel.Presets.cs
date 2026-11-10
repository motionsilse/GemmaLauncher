using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using GemmaLauncher.Core;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

public enum PresetEditDecision { Cancel, Save, Discard }

public sealed partial class LauncherViewModel
{
    private PresetChoice? _editingPreset;
    private readonly HashSet<GlossaryRow> _trackedGlossary = [];
    private bool _loadingPreset;
    public event Action<string, int>? PresetValidationFailed;
    public string PresetValidationMessage { get; private set; } = "";
    public Action? CommitPresetEdits { get; set; }
    public Func<PresetEditDecision>? ResolvePresetEdits { get; set; }
    public Func<string, bool>? ConfirmPresetDeletion { get; set; }
    public ICommand DuplicatePresetCommand { get; private set; } = null!;
    public PresetChoice? EditingPreset
    {
        get => _editingPreset;
        set
        {
            if (ReferenceEquals(value, _editingPreset)) return;
            if (value is null || value.Id == "none" || !SavePendingPreset()) { OnPropertyChanged(); return; }
            LoadPresetEditor(value);
        }
    }
    public bool IsNewPreset => EditingPreset is not null && !Presets.Contains(EditingPreset);
    public bool HasPresetChanges
    {
        get
        {
            if (_loadingPreset || EditingPreset is null) return false;
            var saved = EditingPreset.Preset;
            return IsNewPreset || PresetName != saved.Name || PresetPrompt != saved.Prompt ||
                PresetTemperature != Format(saved.Temperature) || PresetTopP != Format(saved.TopP) || PresetMaxTokens != Format(saved.MaxTokens) ||
                !Glossary.Where(row => row.Source.Length > 0 || row.Target.Length > 0).Select(row => new GlossaryEntry(row.Source.Trim(), row.Target.Trim())).SequenceEqual(saved.Glossary);
        }
    }
    public bool IsPresetShared => EditingPreset is not null && _settings.ModelPresets.Any(pair => pair.Key != SelectedModel?.Id && pair.Value == EditingPreset.Id);
    public string PresetScopeLabel => EditingPreset is null ? T("ui.library.presetsHint") : IsPresetShared
        ? T("ui.preset.shared", string.Join(", ", Models.Where(model => _settings.ModelPresets.GetValueOrDefault(model.Id) == EditingPreset!.Id).Select(model => model.DisplayName)))
        : T(IsNewPreset ? "ui.preset.draftHint" : SelectedPreset?.Id == EditingPreset?.Id ? "ui.preset.applyHint" : "ui.preset.notApplied");
    public string PresetDirtyLabel => HasPresetChanges ? T("ui.preset.unsaved") : "";
    private static string Format(object? value) => value is IFormattable formatted ? formatted.ToString(null, CultureInfo.InvariantCulture) : "";

    private void RefreshPresetProperties()
    {
        if (_loadingPreset) return;
        if (HasPresetChanges && _statusNote.Length > 0) { _statusNote = ""; OnPropertyChanged(nameof(StatusNote)); }
        foreach (var name in new[] { nameof(EditingPreset), nameof(CanEditPreset), nameof(HasPresetChanges), nameof(IsNewPreset), nameof(IsPresetShared), nameof(PresetScopeLabel), nameof(PresetDirtyLabel) }) OnPropertyChanged(name);
        foreach (var command in _commands) command.Refresh();
    }
    private void TrackGlossaryChanges()
    {
        foreach (var row in _trackedGlossary.Except(Glossary).ToArray()) { row.PropertyChanged -= GlossaryRowChanged; _trackedGlossary.Remove(row); }
        foreach (var row in Glossary.Except(_trackedGlossary)) { row.PropertyChanged += GlossaryRowChanged; _trackedGlossary.Add(row); }
        RefreshPresetProperties();
    }
    private void GlossaryRowChanged(object? sender, PropertyChangedEventArgs e) => RefreshPresetProperties();
    private void LoadPresetEditor() => LoadPresetEditor(SelectedPreset is { Id: not "none" } selected ? selected : null);
    private void LoadPresetEditor(PresetChoice? choice)
    {
        _loadingPreset = true;
        try
        {
            _editingPreset = choice;
            var preset = choice?.Preset ?? TaskPreset.Empty;
            PresetName = choice is null ? "" : preset.Name; PresetPrompt = preset.Prompt;
            PresetTemperature = Format(preset.Temperature); PresetTopP = Format(preset.TopP); PresetMaxTokens = Format(preset.MaxTokens);
            Glossary.Clear(); foreach (var entry in preset.Glossary) Glossary.Add(new() { Source = entry.Source, Target = entry.Target });
        }
        finally { _loadingPreset = false; }
        PresetValidationMessage = ""; OnPropertyChanged(nameof(PresetValidationMessage));
        RefreshPresetProperties();
    }
    private void BeginPresetDraft()
    {
        LoadPresetEditor(new(new TaskPreset { Name = T("ui.library.newPreset") }));
        PageIndex = 2;
    }
    private void DuplicatePreset()
    {
        CommitPresetEdits?.Invoke();
        var name = PresetName; var prompt = PresetPrompt; var temperature = PresetTemperature; var topP = PresetTopP; var maxTokens = PresetMaxTokens;
        var rows = Glossary.Select(row => new GlossaryEntry(row.Source, row.Target)).ToArray();
        LoadPresetEditor(new(new TaskPreset { Name = T("ui.preset.copyName", name.Length > 80 ? name[..80] : name), Prompt = prompt, Glossary = rows }));
        PresetTemperature = temperature; PresetTopP = topP; PresetMaxTokens = maxTokens;
    }
    public void DiscardPresetChanges()
    {
        var existing = EditingPreset is not null && Presets.Contains(EditingPreset) ? EditingPreset : null;
        LoadPresetEditor(existing);
    }
    public bool ResolvePendingPreset() => SavePendingPreset();
    private bool SavePendingPreset()
    {
        CommitPresetEdits?.Invoke();
        if (!HasPresetChanges) return true;
        switch (ResolvePresetEdits?.Invoke() ?? PresetEditDecision.Cancel)
        {
            case PresetEditDecision.Save: return SavePreset(false);
            case PresetEditDecision.Discard: DiscardPresetChanges(); return true;
            default: return false;
        }
    }
    public bool SavePresetDraft(bool apply = false) => SavePreset(apply);
    private bool SavePreset(bool apply)
    {
        CommitPresetEdits?.Invoke();
        if (EditingPreset is not { } editing) return true;
        bool Invalid(string field, int row = -1)
        {
            PresetValidationMessage = T("engine.library.invalidPreset"); Note(PresetValidationMessage);
            OnPropertyChanged(nameof(PresetValidationMessage)); PresetValidationFailed?.Invoke(field, row); return false;
        }
        if (string.IsNullOrWhiteSpace(PresetName) || PresetName.Trim().Length > 100 || PresetName.Any(char.IsControl)) return Invalid("PresetName");
        if (PresetPrompt.Length > 100000) return Invalid("PresetPrompt");
        for (var row = 0; row < Glossary.Count; row++)
            if ((Glossary[row].Source.Length > 0 || Glossary[row].Target.Length > 0) && (string.IsNullOrWhiteSpace(Glossary[row].Source) || string.IsNullOrWhiteSpace(Glossary[row].Target) || Glossary[row].Source.Length > 500 || Glossary[row].Target.Length > 500))
                return Invalid("GlossaryGrid", row);
        if (Glossary.Count > 2000) return Invalid("GlossaryGrid", 2000);
        if (!ValidNumber(PresetTemperature, 0, 2)) return Invalid("PresetTemperature");
        if (!ValidNumber(PresetTopP, double.Epsilon, 1)) return Invalid("PresetTopP");
        if (!string.IsNullOrWhiteSpace(PresetMaxTokens) && (!int.TryParse(PresetMaxTokens, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maximum) || maximum is < 1 or > 131072)) return Invalid("PresetMaxTokens");
        try
        {
            var updated = editing.Preset with { Name = PresetName.Trim(), Prompt = PresetPrompt,
                Glossary = Glossary.Where(row => row.Source.Length > 0 || row.Target.Length > 0).Select(row => new GlossaryEntry(row.Source.Trim(), row.Target.Trim())).ToArray(),
                Temperature = Number(PresetTemperature), TopP = Number(PresetTopP), MaxTokens = string.IsNullOrWhiteSpace(PresetMaxTokens) ? null : int.Parse(PresetMaxTokens, CultureInfo.InvariantCulture) };
            updated.Validate();
            var list = _settings.Presets.ToList(); var index = list.FindIndex(preset => preset.Id == updated.Id);
            if (index >= 0) list[index] = updated; else list.Add(updated);
            var references = new Dictionary<string, string>(_settings.ModelPresets);
            if (apply && SelectedModel is not null) references[SelectedModel.Id] = updated.Id;
            _store.Save(_settings with { Presets = list, ModelPresets = references });
            _settings.Presets = list; _settings.ModelPresets = references;
            editing.Preset = updated; editing.RefreshLanguage();
            if (!Presets.Contains(editing)) Presets.Add(editing);
            if (apply) { _selectedPreset = editing; OnPropertyChanged(nameof(SelectedPreset)); }
            LoadPresetEditor(editing); PresetValidationMessage = ""; OnPropertyChanged(nameof(PresetValidationMessage));
            UpdateLivePreset(); RefreshPresetProperties(); Note(T(apply ? "ui.preset.applied" : "ui.library.presetSaved")); return true;
        }
        catch (Exception ex)
        {
            PresetValidationMessage = ex is FormatException or OverflowException ? T("engine.library.invalidPreset") : ex.Message;
            Note(PresetValidationMessage); OnPropertyChanged(nameof(PresetValidationMessage)); PresetValidationFailed?.Invoke("", -1); return false;
        }
        static double? Number(string value) => string.IsNullOrWhiteSpace(value) ? null : double.Parse(value, CultureInfo.InvariantCulture);
        static bool ValidNumber(string text, double minimum, double maximum) => string.IsNullOrWhiteSpace(text) || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= minimum && number <= maximum;
    }
    private void DeletePreset()
    {
        CommitPresetEdits?.Invoke();
        if (EditingPreset is not { } editing || !Presets.Contains(editing) || ConfirmPresetDeletion?.Invoke(editing.Name) != true) return;
        var list = _settings.Presets.Where(preset => preset.Id != editing.Id).ToList();
        var references = _settings.ModelPresets.Where(pair => pair.Value != editing.Id).ToDictionary();
        try { _store.Save(_settings with { Presets = list, ModelPresets = references }); }
        catch (Exception ex) { Note(T("ui.note.saveFailed", ex.Message)); return; }
        _settings.Presets = list; _settings.ModelPresets = references;
        if (SelectedPreset?.Id == editing.Id) { _selectedPreset = Presets[0]; OnPropertyChanged(nameof(SelectedPreset)); }
        Presets.Remove(editing); LoadPresetEditor(null); UpdateLivePreset(); PageIndex = 0; Note(""); Refresh();
    }
    private void UpdateLivePreset()
    {
        if (_session is ServerSession session) session.UpdatePreset(SelectedPreset?.Preset ?? TaskPreset.Empty);
    }
}
