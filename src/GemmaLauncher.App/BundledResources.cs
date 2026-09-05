using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GemmaLauncher.Core;

namespace GemmaLauncher.App;

public static partial class BundledResources
{
    private static readonly Assembly AppAssembly = typeof(BundledResources).Assembly;
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    private static readonly string[] Domains = ["engine", "ui", "models"];

    public static ModelCatalog LoadCatalog()
    {
        using var stream = RequiredResource(AppAssembly, "GemmaLauncher.Catalog.json");
        var catalog = JsonSerializer.Deserialize<ModelCatalog>(stream, CatalogLoader.JsonOptions)
            ?? throw new InvalidDataException("The bundled model catalog is empty.");
        CatalogLoader.Validate(catalog);
        return catalog;
    }

    public static string ReadNotices()
    {
        using var bundled = AppAssembly.GetManifestResourceStream("GemmaLauncher.Notices.txt");
        if (bundled is not null) return ReadText(bundled);
        // Ordinary development builds contain source notices. Release builds require the full runtime bundle.
        using var license = RequiredResource(AppAssembly, "GemmaLauncher.License.txt");
        using var thirdParty = RequiredResource(AppAssembly, "GemmaLauncher.ThirdPartyNotices.txt");
        return ReadText(license) + "\n\n" + ReadText(thirdParty);
    }

    public static int ValidateLocales()
    {
        var assembly = typeof(Core.Localization).Assembly;
        var languages = Core.Localization.Current.Languages;
        if (languages.Count != 17) throw new InvalidDataException("The package must contain 17 languages.");
        var allKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var domain in Domains)
        {
            var english = ReadLocale(assembly, "en", domain);
            if (english.Count == 0 || english.Keys.Any(key => !allKeys.Add(key)))
                throw new InvalidDataException($"The bundled {domain} locale contains missing or duplicate keys.");
            foreach (var language in languages)
            {
                var translated = ReadLocale(assembly, language.Code, domain);
                if (!english.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(translated.Keys))
                    throw new InvalidDataException($"The bundled {language.Code}.{domain} locale has different keys from English.");
                foreach (var (key, value) in english)
                {
                    _ = CompositeFormat.Parse(translated[key]);
                    if (!PlaceholderIndices(value).SetEquals(PlaceholderIndices(translated[key])))
                        throw new InvalidDataException($"The bundled {language.Code} translation has invalid placeholders for {key}.");
                }
            }
        }
        return languages.Count;
    }

    public static bool IsUtilityCommand(string[] args) => args.Contains("--licenses") || args.Contains("--verify-package");

    public static int RunUtilityCommand(string[] args)
    {
        var verifyIndex = Array.IndexOf(args, "--verify-package");
        var reportPath = verifyIndex >= 0 && verifyIndex + 1 < args.Length ? args[verifyIndex + 1] : null;
        try
        {
            if (args.Length == 1 && args[0] == "--licenses")
            {
                var version = AppAssembly.GetName().Version?.ToString(3) ?? "unknown";
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GemmaLauncher", "licenses", version);
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "THIRD-PARTY-NOTICES.txt");
                File.WriteAllText(path, ReadNotices(), new UTF8Encoding(false));
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return 0;
            }
            if (args.Length != 2 || args[0] != "--verify-package" || string.IsNullOrWhiteSpace(reportPath))
                throw new ArgumentException("Use --licenses or --verify-package <reportPath>.");

            var catalog = LoadCatalog();
            var languageCount = ValidateLocales();
            using var noticesStream = RequiredResource(AppAssembly, "GemmaLauncher.Notices.txt");
            var notices = ReadText(noticesStream);
            foreach (var section in new[]
            {
                "[Launcher/LICENSE]", "[Launcher/THIRD-PARTY-NOTICES.md]",
                "[Microsoft.NETCore.App.Runtime.win-x64/LICENSE.TXT]",
                "[Microsoft.WindowsDesktop.App.Runtime.win-x64/LICENSE]"
            })
                if (!notices.Contains(section, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The bundled notices are missing {section}.");

            WriteReport(reportPath, new
            {
                success = true,
                version = AppAssembly.GetName().Version?.ToString(3),
                modelCount = catalog.Models.Length,
                modelIds = catalog.Models.Select(model => model.Id).ToArray(),
                languageCount,
                resourceNames = AppAssembly.GetManifestResourceNames().Concat(typeof(Core.Localization).Assembly.GetManifestResourceNames()).Order(StringComparer.Ordinal).ToArray(),
                noticesLength = notices.Length
            });
            return 0;
        }
        catch (Exception exception)
        {
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                try { WriteReport(reportPath, new { success = false, error = exception.Message }); }
                catch (Exception) { /* An unwritable report path still returns a failure exit code without opening a dialog. */ }
            }
            return 1;
        }
    }

    private static Dictionary<string, string> ReadLocale(Assembly assembly, string language, string domain)
    {
        using var stream = RequiredResource(assembly, $"GemmaLauncher.Core.Locales.{language}.{domain}.json");
        using var document = JsonDocument.Parse(stream);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.Value.GetString()) ||
                !entries.TryAdd(entry.Name, entry.Value.GetString()!))
                throw new InvalidDataException($"The bundled {language}.{domain} locale contains invalid entries.");
            _ = CompositeFormat.Parse(entry.Value.GetString()!);
        }
        return entries;
    }

    private static HashSet<string> PlaceholderIndices(string value) => Placeholders().Matches(value)
        .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static Stream RequiredResource(Assembly assembly, string name) => assembly.GetManifestResourceStream(name)
        ?? throw new InvalidDataException($"The package is missing the embedded resource {name}.");

    private static string ReadText(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static void WriteReport(string path, object report)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, ReportOptions), new UTF8Encoding(false));
    }

    [GeneratedRegex(@"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})")]
    private static partial Regex Placeholders();
}
