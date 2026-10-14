using GemmaLauncher.Core;

namespace GemmaLauncher.App;

public sealed class ModelFamily : ObservableObject
{
    public IReadOnlyList<ModelPresentation> Variants { get; }
    public string Name => Variants[0].Definition.FamilyName.Length > 0 ? Variants[0].Definition.FamilyName : Variants[0].DisplayName;
    public bool HasVariants => Variants.Count > 1;
    private ModelPresentation _selected;
    public ModelPresentation Selected { get => _selected; set { if (value is not null) Set(ref _selected, value); } }
    public ModelFamily(IEnumerable<ModelPresentation> variants) { Variants = variants.ToArray(); _selected = Variants[0]; }
}

public sealed class PresetChoice(TaskPreset preset) : ObservableObject
{
    public TaskPreset Preset { get; set; } = preset;
    public string Id => Preset.Id;
    public string Name => Id == "none" ? Core.Localization.T("ui.library.defaults") : Core.Localization.Current.Retranslate(Preset.Name);
    public void RefreshLanguage() => OnPropertyChanged(nameof(Name));
}

public sealed class GlossaryRow : ObservableObject
{
    private string _source = "";
    private string _target = "";
    public string Source { get => _source; set => Set(ref _source, value); }
    public string Target { get => _target; set => Set(ref _target, value); }
}
