using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

public static class CatalogTests
{
    public static Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var catalog = CatalogLoader.Load(Path.Combine(root, "src/GemmaLauncher.App/Assets/catalog.json"));
        var ordered = catalog.Models.OrderBy(model => model.FamilyOrder).ToArray();
        if (ordered.Length != 10 || !ordered.TakeLast(3).Select(model => model.Id).SequenceEqual(
            ["plamo-3-translate-0.8b", "plamo-3-translate-2b", "anis-trans"]))
            throw new Exception("Both PLaMo choices must appear directly above ANIS in the built-in list.");
        var gemmaModels = new[]
        {
            "translate-gemma4-sub-e2b-q4-k-xl", "translate-gemma4-sub-e4b-q4-k-xl",
            "unsloth-gemma4-qat-e2b-ud-q4-k-xl", "unsloth-gemma4-qat-e4b-ud-q4-k-xl"
        };
        if (!ordered.Take(4).Select(model => model.Id).SequenceEqual(gemmaModels) ||
            !catalog.Models.Take(4).Select(model => model.Id).SequenceEqual(gemmaModels))
            throw new Exception("Both Unsloth QAT models must appear directly below Translate Gemma Sub.");
        foreach (var modelChoice in ordered.Skip(2).Take(2))
        {
            var configuration = new LaunchConfiguration(new(modelChoice, "model.gguf", "mtp.gguf"), "llama-server.exe", modelChoice.Profile.DefaultContext, ".", 8080);
            var arguments = ServerSession.BuildArguments(configuration);
            if (!modelChoice.DisplayName.StartsWith("Unsloth Gemma 4 QAT", StringComparison.Ordinal) ||
                modelChoice.Architecture != "gemma4" || !modelChoice.SupportsTaskPresets ||
                !modelChoice.Artifacts.Single(a => a.Role == "model").Filename.EndsWith("-UD-Q4_K_XL.gguf", StringComparison.Ordinal) ||
                modelChoice.Artifacts.Count(a => a.Role == "mtp") != 1 || !arguments.Contains("--spec-draft-model"))
                throw new Exception("Unsloth QAT must use its own UD-Q4_K_XL weights and matching MTP draft model.");
        }
        foreach (var modelChoice in ordered.TakeLast(3).Take(2))
        {
            var configuration = new LaunchConfiguration(new(modelChoice, "model.gguf", null), "llama-server.exe", modelChoice.Profile.DefaultContext, ".", 8080);
            var arguments = ServerSession.BuildArguments(configuration);
            if (modelChoice.Engine != "llama" || modelChoice.Architecture != "plamo3" || modelChoice.Artifacts.Length != 1 ||
                modelChoice.SupportsTaskPresets || !arguments.Contains("--no-prefill-assistant") || arguments.Contains("--spec-draft-model"))
                throw new Exception("PLaMo must load the official single GGUF with its translation template and no MTP.");
            var otherPlamo = modelChoice with { FamilyId = "user-plamo", Origin = "local" };
            var otherArguments = ServerSession.BuildArguments(configuration with { Model = new(otherPlamo, "model.gguf", null) });
            if (!otherPlamo.SupportsTaskPresets || otherArguments.Contains("--no-prefill-assistant"))
                throw new Exception("A shared architecture alone must not impose the PLaMo Translate prompt contract.");
        }
        if (catalog.Models[0].Profile.Lazy != "on" || catalog.Models[1].Profile.Lazy != "on" || catalog.Models.Single(model => model.FamilyId == "gemma-creative").Profile.MtpDraftMax != 4) throw new Exception("Model defaults were lost.");
        foreach (var hy in catalog.Models.Where(model => model.FamilyId == "hy-mt2"))
            if (hy.Profile.Lazy != "off" || hy.Artifacts.Length != 1 || hy.Profile.MtpDraftMax != 0 || hy.Profile.TopP != .6 || hy.Profile.Temperature != .7 || hy.Profile.RepeatPenalty != 1.05)
                throw new Exception("HY-MT2 must retain independent translation defaults without MTP.");
        MustReject(catalog with { Models = [catalog.Models[0], catalog.Models[0]] });
        var model = catalog.Models[0];
        MustReject(catalog with { Models = [model with { Artifacts = [model.Artifacts[0] with { Filename = "../escape.gguf" }] }] });
        MustReject(catalog with { Models = [model with { Profile = model.Profile with { DefaultContext = 777 } }] });
        MustReject(catalog with { Models = [model with { Profile = model.Profile with { Temperature = double.NaN } }] });
        MustReject(catalog with { Models = [model with { Profile = model.Profile with { MtpDraftMax = 0 } }] });
        foreach (var invalidId in new[] { "model.", "model\n", "model ", "../model", ".", ".." })
        {
            MustReject(catalog with { Models = [model with { Id = invalidId }] });
            MustReject(catalog with { Runtime = catalog.Runtime with { Version = invalidId } });
        }
        MustReject(catalog with { Runtime = catalog.Runtime with { Sha256 = catalog.Runtime.Sha256 + "\n" } });
        MustReject(catalog with { Models = [model with { Artifacts = model.Artifacts.Select(artifact => artifact with { Sha256 = artifact.Sha256 + "\n" }).ToArray() }] });
        var folder = Path.Combine(root, "artifacts/tests/settings-" + Guid.NewGuid().ToString("N"));
        var paths = new LauncherPaths(folder);
        var store = new SettingsStore(paths);
        var original = Path.Combine(folder, "renamed model.gguf");
        var settings = new LauncherSettings { SelectedModelId = model.Id, Contexts = new() { [model.Id] = 16384 }, ModelFolders = [folder], ModelFiles = new() { [model.Artifacts[0].Sha256] = original } };
        store.Save(settings);
        var loaded = store.Load();
        if (loaded.SelectedModelId != model.Id || loaded.Contexts[model.Id] != 16384) throw new Exception("Settings round trip failed.");
        if (loaded.ModelFiles[model.Artifacts[0].Sha256] != original) throw new Exception("Selected model file mapping did not survive a settings reload.");
        File.WriteAllText(paths.SettingsFile, "{\"languagePreference\":\"ko\",\"modelFolders\":[\"saved-folder\"]}");
        var legacy = store.Load();
        if (legacy.LanguagePreference != "ko" || legacy.ModelFolders.Single() != "saved-folder" || legacy.ModelFiles.Count != 0) throw new Exception("Legacy settings must retain preferences and start with empty model file mappings.");
        File.WriteAllText(paths.SettingsFile, "{\"modelFiles\":null}");
        if (store.Load().ModelFiles.Count != 0) throw new Exception("Null model file mappings must recover to an empty dictionary.");
        File.WriteAllText(paths.SettingsFile, "broken json");
        if (store.Load().Contexts.Count != 0 || !File.Exists(paths.SettingsFile + ".backup")) throw new Exception("Damaged settings did not recover.");
        return Task.CompletedTask;
    }
    private static void MustReject(ModelCatalog catalog)
    {
        try { CatalogLoader.Validate(catalog); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid catalog was accepted.");
    }
}
