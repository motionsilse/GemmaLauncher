using System.Security.Cryptography;

namespace GemmaLauncher.Core;

public sealed record ImportedModel(ModelDefinition Definition, Dictionary<string, string> Files);

public static class ModelImportService
{
    public static async Task<ModelArtifact> ImportMtpAsync(string modelPath, string path, CancellationToken token = default)
    {
        if (!Path.GetExtension(path).Equals(".gguf", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Localization.T("engine.library.invalidGguf"));
        var model = await Task.Run(() => GgufMetadata.Read(modelPath, token), token);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1_048_576, true);
        var companion = await Task.Run(() => GgufMetadata.Read(stream, token), token);
        GgufMetadata.RequireMtpCompatible(model, companion);
        stream.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
        return new ModelArtifact { Role = "mtp", Filename = Path.GetFileName(path), Bytes = stream.Length, Sha256 = hash, Url = "", IsLocal = true };
    }

    public static async Task<ImportedModel> ImportLocalAsync(string path, IEnumerable<ModelDefinition> knownModels,
        CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        if (!Path.GetExtension(path).Equals(".gguf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Localization.T("engine.library.invalidGguf"));
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1_048_576, true);
        var metadata = await Task.Run(() => GgufMetadata.Read(stream, cancellationToken), cancellationToken);
        metadata.RequireLoadable();
        stream.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        var existing = knownModels.FirstOrDefault(model => model.Artifacts.Any(a => a.Role == "model" && a.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase)));
        var artifact = new ModelArtifact { Role = "model", Filename = Path.GetFileName(path), Url = "", Sha256 = hash, Bytes = stream.Length, IsLocal = true };
        return new(existing ?? CreateDefinition(metadata, artifact, "local", "", ""), new() { [hash] = path });
    }

    public static ModelDefinition CreateDefinition(GgufMetadata metadata, ModelArtifact artifact, string origin, string source, string license)
    {
        metadata.RequireLoadable();
        var context = Math.Clamp(metadata.ContextLength, 512, 32768);
        var choices = new[] { 2048, 4096, 8192, 16384, 32768 }.Where(value => value <= context).Append(context).Distinct().Order().ToArray();
        var name = string.IsNullOrWhiteSpace(metadata.Name) ? Path.GetFileNameWithoutExtension(artifact.Filename) : metadata.Name;
        return new()
        {
            Id = "user-" + artifact.Sha256[..20], ApiModelId = "user-" + artifact.Sha256[..20], DisplayName = name,
            FamilyId = "user-" + artifact.Sha256[..20], Origin = origin, Architecture = metadata.Architecture, HasChatTemplate = metadata.ChatTemplate.Length > 0,
            Category = "GGUF", Headline = name, Description = artifact.Filename,
            HardwareGuide = "", MemoryGuide = "", SourceUrl = source, LicenseUrl = license,
            Profile = new() { ContextSizes = choices, DefaultContext = Math.Min(context, 8192), Lazy = "off", MtpDraftMax = 0, Temperature = .7, TopP = .9, TopK = 40, MinP = .05 },
            Artifacts = [artifact]
        };
    }
}
