using System.Globalization;

namespace GemmaLauncher.Core;

public sealed record ModelLoadOptions
{
    public string GpuLayers { get; init; } = "auto";
    public string CacheType { get; init; } = "f16";
    public string FlashAttention { get; init; } = "auto";
    public string Reasoning { get; init; } = "auto";
    public bool UseMtp { get; init; } = true;
    public int DraftTokens { get; init; } = 2;
    public string ChatTemplate { get; init; } = "";

    public static ModelLoadOptions For(ModelDefinition model) => new()
    {
        CacheType = model.Origin == "catalog" ? "q8_0" : "f16",
        FlashAttention = model.Origin == "catalog" ? "on" : "auto",
        Reasoning = model.Origin == "catalog" ? "off" : "auto",
        DraftTokens = Math.Max(1, model.Profile.MtpDraftMax)
    };

    public void Validate()
    {
        if (GpuLayers is not ("auto" or "all") &&
            (!int.TryParse(GpuLayers, NumberStyles.None, CultureInfo.InvariantCulture, out var layers) || layers is < 0 or > 1000) ||
            CacheType is not ("f16" or "q8_0" or "q4_0") || FlashAttention is not ("on" or "off" or "auto") ||
            Reasoning is not ("on" or "off" or "auto") || DraftTokens is < 1 or > 16 ||
            ChatTemplate is null || ChatTemplate.Length > 14_000 || ChatTemplate.Contains('\0'))
            throw new InvalidDataException(Localization.T("engine.library.invalidOptions"));
        if (CacheType != "f16" && FlashAttention == "off")
            throw new InvalidDataException(Localization.T("engine.library.cacheFlash"));
    }
}
