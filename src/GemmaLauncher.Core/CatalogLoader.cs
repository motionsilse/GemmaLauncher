using System.Text.Json;
using System.Text.RegularExpressions;

namespace GemmaLauncher.Core;

public static partial class CatalogLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static ModelCatalog Load(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 2_000_000) throw new InvalidDataException(Localization.T("engine.catalog.tooLarge"));
        var catalog = JsonSerializer.Deserialize<ModelCatalog>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException(Localization.T("engine.catalog.unreadable"));
        Validate(catalog);
        return catalog;
    }

    public static void Validate(ModelCatalog catalog)
    {
        if (catalog.SchemaVersion != 1 || catalog.Models is not { Length: > 0 and <= 100 })
            throw new InvalidDataException(Localization.T("engine.catalog.unsupported"));
        ValidateDownload(catalog.Runtime.Url, catalog.Runtime.Sha256, catalog.Runtime.Bytes);
        if (!SafeIdentifier(catalog.Runtime.Version) || Path.GetFileName(catalog.Runtime.ExecutableRelativePath) != catalog.Runtime.ExecutableRelativePath)
            throw new InvalidDataException(Localization.T("engine.catalog.invalidRuntime"));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in catalog.Models)
        {
            if (!SafeIdentifier(model.Id) || !ids.Add(model.Id) || string.IsNullOrWhiteSpace(model.DisplayName) ||
                string.IsNullOrWhiteSpace(model.ApiModelId) || model.ApiModelId.Any(char.IsControl) || model.ApiModelId.Contains(','))
                throw new InvalidDataException(Localization.T("engine.catalog.invalidIdentity"));
            if (model.Artifacts is not { Length: >= 1 and <= 8 } || model.Artifacts.Count(a => a.Role == "model") != 1 ||
                model.Artifacts.Count(a => a.Role == "mtp") > 1 || model.Artifacts.Any(a => a.Role is not ("model" or "mtp")))
                throw new InvalidDataException(Localization.T("engine.catalog.invalidFiles"));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var artifact in model.Artifacts)
            {
                if (!SafeFilename(artifact.Filename) || !names.Add(artifact.Filename))
                    throw new InvalidDataException(Localization.T("engine.catalog.invalidFilename"));
                ValidateDownload(artifact.Url, artifact.Sha256, artifact.Bytes);
            }
            var p = model.Profile;
            if (p.ContextSizes is not { Length: > 0 and <= 16 } || !p.ContextSizes.Contains(p.DefaultContext) ||
                p.ContextSizes.Any(n => n < 512 || n > 262144) || p.Lazy is not ("on" or "off" or "auto") ||
                p.MtpDraftMax is < 0 or > 16 || p.Temperature is < 0 or > 2 || !double.IsFinite(p.Temperature) ||
                p.TopP is <= 0 or > 1 || !double.IsFinite(p.TopP) || p.TopK is < 1 or > 1000 ||
                p.MinP is < 0 or > 1 || !double.IsFinite(p.MinP) ||
                (p.MtpDraftMax > 0 != model.Artifacts.Any(a => a.Role == "mtp")))
                throw new InvalidDataException(Localization.T("engine.catalog.invalidProfile"));
            ValidateWebLink(model.SourceUrl);
            ValidateWebLink(model.LicenseUrl);
        }
    }

    public static bool SafeFilename(string name) => !string.IsNullOrWhiteSpace(name) && name.Length < 200 &&
        name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !name.Contains(':') && !name.EndsWith('.') && !name.EndsWith(' ') && name is not ("." or "..");

    private static void ValidateDownload(string url, string hash, long bytes)
    {
        ValidateWebLink(url);
        if (!Sha256().IsMatch(hash) || bytes <= 0 || bytes > 1_000_000_000_000)
            throw new InvalidDataException(Localization.T("engine.catalog.invalidDownload"));
    }

    public static void ValidateWebLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException(Localization.T("engine.catalog.httpsRequired"));
    }

    private static bool SafeIdentifier(string id) => SafeFilename(id) && SafeId().IsMatch(id);

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}\\z")]
    private static partial Regex SafeId();
    [GeneratedRegex("^[a-fA-F0-9]{64}\\z")]
    private static partial Regex Sha256();
}
