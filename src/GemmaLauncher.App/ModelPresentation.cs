using GemmaLauncher.Core;

namespace GemmaLauncher.App;

public enum ModelFileState { Unchecked, Checking, Ready, DownloadNeeded, ReconnectRequired }

/// <summary>Localized presentation without modifying persistent model definitions.</summary>
public sealed class ModelPresentation(ModelDefinition definition) : ObservableObject
{
    private ModelFileState _fileState;
    public ModelFileState FileState
    {
        get => _fileState;
        set { if (Set(ref _fileState, value)) OnPropertyChanged(nameof(FileStateLabel)); }
    }
    public string FileStateLabel => Core.Localization.T(FileState switch
    {
        ModelFileState.Checking => "ui.status.scanning",
        ModelFileState.Ready => "ui.library.filesReady",
        ModelFileState.DownloadNeeded => "ui.library.downloadNeeded",
        ModelFileState.ReconnectRequired => "ui.library.reconnectNeeded",
        _ => "ui.library.checkFiles"
    });
    public ModelDefinition Definition { get; } = definition;
    public string DisplayName => Definition.DisplayName;
    public string Variant => Definition.Variant.Length > 0 ? Definition.Variant : DisplayName;
    public bool IsUserModel => Definition.Origin != "catalog";
    public string OriginLabel => Core.Localization.T(IsUserModel ? "ui.library.userModel" : "ui.library.recommended");
    public string? Credit => Definition.Credit;
    public string DownloadLabel => Definition.DownloadLabel;
    public string Category => Text("category", Definition.Category);
    public string Headline => Text("headline", Definition.Headline);
    public string Description => Text("description", Definition.Description);
    public string HardwareGuide => Text("hardwareGuide", Definition.HardwareGuide);
    public string MemoryGuide => Definition.MemoryGuide.Length == 0 ? Core.Localization.T("ui.library.liveMemory") : Text("memoryGuide", Definition.MemoryGuide);
    public string[] Benefits => Definition.Benefits.Select((text, index) => Text($"benefit{index + 1}", text)).ToArray();

    private string Text(string field, string fallback)
    {
        var prefix = Definition.LocalizationPrefix.Length > 0 ? Definition.LocalizationPrefix : Definition.Id switch
        {
            "translate-gemma4-sub-e2b-q4-k-xl" => "models.e2b",
            "translate-gemma4-sub-e4b-q4-k-xl" => "models.e4b",
            "gemma4-12b-qat-heretic-styletune-q4-q8head" => "models.12b",
            _ => null
        };
        if (prefix is null) return fallback;
        var key = $"{prefix}.{field}";
        var translated = Core.Localization.Current[key];
        return translated == key ? fallback : translated;
    }

    public void RefreshLanguage()
    {
        foreach (var property in new[] { nameof(FileStateLabel), nameof(OriginLabel), nameof(Category), nameof(Headline), nameof(Description), nameof(HardwareGuide), nameof(MemoryGuide), nameof(Benefits) })
            OnPropertyChanged(property);
    }
}
