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
    public string DisplayName => Definition.Engine == "anis" ? Text("name", Definition.DisplayName) : Definition.DisplayName;
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
    public string ResourceGuide
    {
        get
        {
            var lines = new List<string>
            {
                Core.Localization.T("ui.resources.storage", Definition.DownloadBytes > 0 ? DownloadLabel : Core.Localization.T("ui.resources.sizeUnknown"))
            };
            // Model files are kept once; local links are not copied. Reserve 2 GB
            // for runtime extraction, caches and installation overhead, not as a
            // claim about exact installed runtime size.
            var downloads = Definition.Artifacts.Where(a => !a.IsLocal).ToArray();
            if (downloads.All(a => a.Bytes > 0))
            {
                var freeGb = Math.Ceiling((downloads.Sum(a => a.Bytes) / 1_000_000_000d + 2) * 10) / 10;
                lines.Add(Core.Localization.T("ui.resources.install", $"{freeGb:0.0} GB"));
            }
            if (Definition.Artifacts.Any(a => a.IsLocal)) lines.Add(Core.Localization.T("ui.resources.linked"));
            var r = Definition.Resources;
            if (r is not null && Valid(r.RamMinGb, r.RamMaxGb) && Valid(r.VramMinGb, r.VramMaxGb))
            {
                lines.Add($"RAM ≈ {Range(r.RamMinGb, r.RamMaxGb)} GB · VRAM ≈ {Range(r.VramMinGb, r.VramMaxGb)} GB");
                lines.Add(Definition.Engine == "anis" ? Core.Localization.T("ui.resources.anisBasis")
                    : Core.Localization.T("ui.resources.basis", Definition.Profile.DefaultContext.ToString("N0")));
            }
            else lines.Add(Core.Localization.T("ui.resources.unknown"));
            return string.Join("\n",lines);
        }
    }
    private static bool Valid(double min, double max) => double.IsFinite(min) && double.IsFinite(max) && min > 0 && max >= min;
    private static string Range(double min, double max) => min == max ? min.ToString("0.#") : $"{min:0.#}–{max:0.#}";
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
        foreach (var property in new[] { nameof(DisplayName), nameof(FileStateLabel), nameof(OriginLabel), nameof(Category), nameof(Headline), nameof(Description), nameof(HardwareGuide), nameof(MemoryGuide), nameof(ResourceGuide), nameof(Benefits) })
            OnPropertyChanged(property);
    }
}
