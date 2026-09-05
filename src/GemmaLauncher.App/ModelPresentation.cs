using GemmaLauncher.Core;

namespace GemmaLauncher.App;

/// <summary>Localized presentation without modifying persistent model definitions.</summary>
public sealed class ModelPresentation(ModelDefinition definition) : ObservableObject
{
    public ModelDefinition Definition { get; } = definition;
    public string DisplayName => Definition.DisplayName;
    public string? Credit => Definition.Credit;
    public string DownloadLabel => Definition.DownloadLabel;
    public string Category => Text("category", Definition.Category);
    public string Headline => Text("headline", Definition.Headline);
    public string Description => Text("description", Definition.Description);
    public string HardwareGuide => Text("hardwareGuide", Definition.HardwareGuide);
    public string MemoryGuide => Text("memoryGuide", Definition.MemoryGuide);
    public string[] Benefits => Definition.Benefits.Select((text, index) => Text($"benefit{index + 1}", text)).ToArray();

    private string Text(string field, string fallback)
    {
        var prefix = Definition.Id switch
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
        foreach (var property in new[] { nameof(Category), nameof(Headline), nameof(Description), nameof(HardwareGuide), nameof(MemoryGuide), nameof(Benefits) })
            OnPropertyChanged(property);
    }
}
