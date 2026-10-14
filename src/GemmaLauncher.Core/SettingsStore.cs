using System.Text.Json;

namespace GemmaLauncher.Core;

public sealed record LauncherSettings
{
    public string LanguagePreference { get; set; } = "auto";
    public string? SelectedModelId { get; set; }
    public Dictionary<string, int> Contexts { get; set; } = [];
    public List<string> ModelFolders { get; set; } = [];
    public Dictionary<string, string> ModelFiles { get; set; } = [];
    public List<string> LibraryModelIds { get; set; } = [];
    public List<TaskPreset> Presets { get; set; } = [];
    public bool PresetsInitialized { get; set; }
    public Dictionary<string, string> ModelPresets { get; set; } = [];
    public Dictionary<string, ModelLoadOptions> LoadOptions { get; set; } = [];
    public List<ModelDefinition> UserModels { get; set; } = [];
    public Dictionary<string, ModelArtifact> MtpFiles { get; set; } = [];
}

public sealed class SettingsStore(LauncherPaths paths)
{
    public LauncherSettings Load()
    {
        if (!File.Exists(paths.SettingsFile)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(paths.SettingsFile), CatalogLoader.JsonOptions) ?? new();
            settings.Contexts ??= [];
            settings.ModelFolders ??= [];
            settings.ModelFiles ??= [];
            settings.LibraryModelIds ??= [];
            settings.Presets ??= [];
            settings.ModelPresets ??= [];
            settings.LoadOptions ??= [];
            settings.UserModels ??= [];
            settings.MtpFiles ??= [];
            settings.Presets = settings.Presets.Where(preset =>
            {
                try { preset.Validate(); return true; } catch (Exception ex) when (ex is InvalidDataException or NullReferenceException) { return false; }
            }).DistinctBy(preset => preset.Id).ToList();
            foreach (var (id, options) in settings.LoadOptions.ToArray())
            {
                try { options.Validate(); } catch (Exception ex) when (ex is InvalidDataException or NullReferenceException) { settings.LoadOptions.Remove(id); }
            }
            settings.LanguagePreference = string.IsNullOrWhiteSpace(settings.LanguagePreference)
                ? "auto" : settings.LanguagePreference;
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Keep the damaged file available for diagnosis; a later save replaces it atomically.
            try { File.Copy(paths.SettingsFile, paths.SettingsFile + ".backup", true); } catch (IOException) { }
            return new();
        }
    }

    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(paths.Root);
        var temp = paths.SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, CatalogLoader.JsonOptions));
        File.Move(temp, paths.SettingsFile, true);
    }
}
