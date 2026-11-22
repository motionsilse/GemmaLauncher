namespace GemmaLauncher.Core;

/// <summary>Identifies model cache files and the verified main file for explicit deletion.</summary>
public static class ModelCache
{
    internal static string GgufPath(LauncherPaths paths, ModelDefinition model, ModelArtifact artifact) =>
        Path.Combine(paths.Models, model.Id, artifact.Sha256[..12], artifact.Filename);

    internal static string AnisDirectory(LauncherPaths paths) => Path.Combine(paths.Models, "anis-trans", AnisBackend.ModelRevision);

    public static string[] PresentFiles(LauncherPaths paths, ModelDefinition model)
    {
        if (!CatalogLoader.SafeFilename(model.Id)) return [];
        if (model.Engine == "anis")
            return model.Id == "anis-trans" ? AnisBackend.RuntimeModelFiles.Select(file => Path.Combine(AnisDirectory(paths), file.Path)).Where(File.Exists).ToArray() : [];

        return model.Artifacts.Where(artifact => !artifact.IsLocal && CatalogLoader.SafeFilename(artifact.Filename) &&
                artifact.Sha256.Length == 64 && artifact.Sha256.All(Uri.IsHexDigit))
            .Select(artifact => GgufPath(paths, model, artifact))
            .SelectMany(file => new[] { file, file + ".part" })
            .Where(File.Exists).ToArray();
    }

    public static string[] DeletionCandidates(LauncherPaths paths, ModelDefinition model, InstalledModel? installed)
    {
        var files = PresentFiles(paths, model);
        // The model may have been verified in a search folder. Its main file is
        // selectable for deletion, while its neighboring files may be shared.
        if (installed is null || installed.Definition.Id != model.Id || !File.Exists(installed.ModelPath)) return files;
        return files.Append(Path.GetFullPath(installed.ModelPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void DeleteFiles(LauncherPaths paths, ModelDefinition model, InstalledModel? installed = null, IReadOnlyList<string>? confirmed = null)
    {
        var files = DeletionCandidates(paths, model, installed);
        if (confirmed is not null) files = files.Intersect(confirmed, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var file in files) InstallationService.RejectReparsePoints(file);
        foreach (var file in files)
        {
            InstallationService.RejectReparsePoints(file);
            File.Delete(file);
        }
    }
}
