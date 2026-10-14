using System.Globalization;
using System.Text.Json;
using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

public static class LocalizationTests
{
    public static Task RunAsync()
    {
        var expected = new[] { "ko", "en", "ja", "zh-cn", "zh-tw", "es", "pt", "fr", "de", "fil", "vi", "ru", "pl", "id", "ms", "tr", "th" };
        Assert(Localization.Current.Languages.Select(language => language.Code).SequenceEqual(expected), "The language picker must expose all 17 supported languages.");
        Assert(Localization.Current.Languages.All(language => !string.IsNullOrWhiteSpace(language.Name)), "Each language needs a native display name.");
        var mappings = new Dictionary<string, string>
        {
            [""] = "en", ["unsupported-ZZ"] = "en", ["en-US"] = "en", ["KO_kr"] = "ko", ["ja-JP"] = "ja",
            ["zh"] = "zh-cn", ["zh-CN"] = "zh-cn", ["zh-SG"] = "zh-cn", ["zh-Hans"] = "zh-cn",
            ["zh-Hant"] = "zh-tw", ["zh-TW"] = "zh-tw", ["zh-HK"] = "zh-tw", ["zh-MO"] = "zh-tw",
            ["tl-PH"] = "fil", ["fil-PH"] = "fil", ["pt-BR"] = "pt", ["es-MX"] = "es", ["fr-CA"] = "fr",
            ["de-DE"] = "de", ["vi-VN"] = "vi", ["ru-RU"] = "ru", ["pl-PL"] = "pl", ["id-ID"] = "id",
            ["ms-MY"] = "ms", ["tr-TR"] = "tr", ["th-TH"] = "th"
        };
        foreach (var (input, result) in mappings) Assert(Localization.ResolveLanguage(input) == result, "Language mapping failed: " + input);
        Assert(Localization.ResolveLanguage(null) == "en", "A missing OS language must fall back to English.");

        var previousSelection = Localization.Current.Selection;
        try
        {
            foreach (var code in expected)
            {
                Localization.Current.SetLanguage(code);
                Assert(Localization.Current.LanguageCode == code, "An explicit supported language must stay selected.");
                Assert(Localization.T("engine.session.stopped") != "engine.session.stopped", "Embedded English engine fallback is missing.");
            }
        }
        finally { Localization.Current.SetLanguage(previousSelection); }

        CheckFallbacksAndNotifications();
        CheckRetranslation();
        CheckStartupErrorRetranslation();
        CheckSettings();
        CheckEmbeddedTranslations();
        return Task.CompletedTask;
    }

    private static void CheckEmbeddedTranslations()
    {
        var previous = Localization.Current.Selection;
        try
        {
            foreach (var language in Localization.Current.Languages)
            {
                Localization.Current.SetLanguage(language.Code);
                foreach (var domain in new[] { "engine", "ui", "models" })
                {
                    var name = $"GemmaLauncher.Core.Locales.{language.Code}.{domain}.json";
                    using var resource = typeof(Localization).Assembly.GetManifestResourceStream(name);
                    Assert(resource is not null, "A shipping translation domain was not embedded: " + name);
                    using var document = JsonDocument.Parse(resource!);
                    foreach (var entry in document.RootElement.EnumerateObject())
                        Assert(Localization.Current[entry.Name] == entry.Value.GetString(), "The selected embedded translation was replaced by fallback: " + name + ": " + entry.Name);
                }
            }
        }
        finally { Localization.Current.SetLanguage(previous); }
    }

    private static void CheckFallbacksAndNotifications()
    {
        var localizer = new Localization("ko-KR", Documents);
        Assert(localizer.Selection == "auto" && localizer.LanguageCode == "ko", "Auto must initially use the captured OS language.");
        List<string?> notifications = [];
        var languageEvents = 0;
        localizer.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        localizer.LanguageChanged += (_, _) => languageEvents++;
        localizer.SetLanguage("ja-JP");
        Assert(localizer["test.message"] == "日本語 {0}", "A valid localized entry must override English.");
        Assert(localizer["test.fallback"] == "English fallback", "Missing entries must retain English.");
        Assert(localizer["test.broken"] == "Count {0}", "Malformed placeholders must retain English.");
        Assert(localizer["test.wrongArguments"] == "Count {0}", "Missing placeholder arguments must retain English.");
        Assert(localizer["test.unknown"] == "test.unknown", "Unknown keys must be returned unchanged.");
        Assert(localizer["test.secondDomain"] == "A second domain", "One malformed domain must not suppress other resources.");
        Assert(notifications.Contains("Item[]") && notifications.Contains("LanguageCode") && notifications.Contains("Selection") && languageEvents == 1,
            "A language change must notify both WPF bindings and subscribers.");
        localizer.SetLanguage("fr");
        Assert(localizer.Text("test.number", 1.5) == "Value 1,50", "Parameterized messages must format numbers for the selected language.");
        Assert(localizer.Text("test.numericFormat", 7) == "Count 7", "Invalid translated numeric format strings must fall back safely.");
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            localizer.SetLanguage("auto");
            Assert(localizer.LanguageCode == "ko", "Auto must retain the original OS language after other culture changes.");
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
        localizer.SetLanguage("unknown-language");
        Assert(localizer.Selection == "en" && localizer.LanguageCode == "en", "Unsupported explicit selections must normalize to English.");
    }

    private static void CheckRetranslation()
    {
        var localizer = new Localization("en-US", Documents);
        var inner = localizer.Text("test.message", "model.gguf");
        var outer = localizer.Text("test.wrapper", inner);
        localizer.SetLanguage("ko");
        Assert(localizer.Retranslate(inner) == "한국어 model.gguf", "A generated status snapshot must update to the selected language.");
        Assert(localizer.Retranslate(outer) == "안내: 한국어 model.gguf", "Nested generated message arguments must retain their provenance.");
        Assert(localizer.Retranslate("Native error: " + inner) == "Native error: " + inner, "External or composed payloads must remain unchanged.");
        var korean = localizer.Retranslate(inner);
        localizer.SetLanguage("ja");
        Assert(localizer.Retranslate(korean) == "日本語 model.gguf", "Repeated language changes must preserve generated-message provenance.");
        for (var index = 0; index < 600; index++) localizer.Text("test.message", index);
        Assert(localizer.Retranslate(inner) == inner, "The generated-message cache must have a finite capacity.");
    }

    private static void CheckSettings()
    {
        Assert(new LauncherSettings().LanguagePreference == "auto", "Fresh settings must default to OS language detection.");
        var root = Path.Combine(Path.GetTempPath(), "GemmaLauncher-localization-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new LauncherPaths(root);
            var store = new SettingsStore(paths);
            store.Save(new LauncherSettings { LanguagePreference = "zh-tw" });
            Assert(store.Load().LanguagePreference == "zh-tw", "The selected language must survive settings persistence.");
            File.WriteAllText(paths.SettingsFile, "{\"SelectedModelId\":\"existing-model\"}");
            Assert(store.Load().LanguagePreference == "auto", "Existing settings without a language must remain compatible.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void CheckStartupErrorRetranslation()
    {
        var previous = Localization.Current.Selection;
        try
        {
            Localization.Current.SetLanguage("ko");
            var reason = Localization.T("engine.session.startupTimeout");
            var message = Localization.T("engine.session.failureLog", reason, @"D:\logs\server.log");
            Localization.Current.SetLanguage("en");
            var expected = Localization.T("engine.session.failureLog", Localization.T("engine.session.startupTimeout"), @"D:\logs\server.log");
            Assert(Localization.Current.Retranslate(message) == expected, "Both the startup error and its log label must change language without altering the log path.");
        }
        finally { Localization.Current.SetLanguage(previous); }
    }

    private static IEnumerable<string> Documents(string code)
    {
        if (code == "en") return ["""
            {"test.message":"English {0}","test.wrapper":"Notice: {0}","test.fallback":"English fallback","test.broken":"Count {0}","test.wrongArguments":"Count {0}","test.number":"Value {0:0.00}","test.numericFormat":"Count {0:D}"}
            """, """{"test.secondDomain":"A second domain"}"""];
        if (code == "ko") return ["""{"test.message":"한국어 {0}","test.wrapper":"안내: {0}"}"""];
        if (code == "ja") return ["broken json", """{"test.message":"日本語 {0}","test.broken":"壊れた {","test.wrongArguments":"数"}"""];
        if (code == "fr") return ["""{"test.numericFormat":"Nombre {0:Q}"}"""];
        return [];
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
