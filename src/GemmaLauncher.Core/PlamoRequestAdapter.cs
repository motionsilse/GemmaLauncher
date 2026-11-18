using System.Text.Json.Nodes;

namespace GemmaLauncher.Core;

/// <summary>Converts explicitly labelled source envelopes into PLaMo template inputs.</summary>
internal static class PlamoRequestAdapter
{
    private const string Open = "<source_text>";
    private const string Close = "</source_text>";
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Japanese"] = "ja", ["English"] = "en", ["Korean"] = "ko",
        ["Simplified Chinese"] = "zh-hans", ["Chinese (Simplified)"] = "zh-hans",
        ["Traditional Chinese"] = "zh-hant", ["Chinese (Traditional)"] = "zh-hant",
        ["Arabic"] = "ar", ["Azerbaijani"] = "az", ["Belarusian"] = "be", ["Bulgarian"] = "bg",
        ["Bengali"] = "bn", ["Catalan"] = "ca", ["Cebuano"] = "ceb", ["German"] = "de", ["Spanish"] = "es",
        ["Estonian"] = "et", ["Basque"] = "eu", ["Persian"] = "fa", ["French"] = "fr", ["Gujarati"] = "gu",
        ["Hebrew"] = "he", ["Hindi"] = "hi", ["Croatian"] = "hr", ["Hungarian"] = "hu", ["Armenian"] = "hy",
        ["Indonesian"] = "id", ["Italian"] = "it", ["Javanese"] = "jv", ["Georgian"] = "ka", ["Kazakh"] = "kk",
        ["Khmer"] = "km", ["Lithuanian"] = "lt", ["Latvian"] = "lv", ["Malayalam"] = "ml", ["Marathi"] = "mr",
        ["Malay"] = "ms", ["Burmese"] = "my", ["Myanmar"] = "my", ["Nepali"] = "ne", ["Dutch"] = "nl",
        ["Punjabi"] = "pa", ["Polish"] = "pl", ["Russian"] = "ru", ["Sinhala"] = "si", ["Slovak"] = "sk",
        ["Slovenian"] = "sl", ["Serbian"] = "sr", ["Swahili"] = "sw", ["Tamil"] = "ta", ["Telugu"] = "te",
        ["Thai"] = "th", ["Tagalog"] = "tl", ["Filipino"] = "tl", ["Filipino (Tagalog)"] = "tl",
        ["Urdu"] = "ur", ["Uzbek"] = "uz", ["Vietnamese"] = "vi",
        ["zh"] = "zh-hans", ["zh-cn"] = "zh-hans", ["zh-tw"] = "zh-hant", ["fil"] = "tl", ["iw"] = "he", ["in"] = "id"
    };
    private static readonly HashSet<string> Codes = new(Languages.Values, StringComparer.OrdinalIgnoreCase);

    public static void Apply(JsonObject request)
    {
        if (request["messages"] is not JsonArray messages) return;
        var user = messages.OfType<JsonObject>().LastOrDefault(message => message["role"]?.ToString() == "user");
        if (user?["content"] is not JsonValue value || !value.TryGetValue<string>(out var content)) return;
        var text = content.TrimEnd();
        var opening = text.IndexOf(Open, StringComparison.Ordinal);
        if (opening < 0 || !text.EndsWith(Close, StringComparison.Ordinal)) return;
        var header = text[..opening];
        var target = ReadField(header, "Target language");
        if (target is null) return;
        var source = ReadField(header, "Source language");
        var original = text[(opening + Open.Length)..^Close.Length];
        if (original.StartsWith("\r\n", StringComparison.Ordinal)) original = original[2..];
        else if (original.StartsWith('\n')) original = original[1..];
        if (original.EndsWith("\r\n", StringComparison.Ordinal)) original = original[..^2];
        else if (original.EndsWith('\n')) original = original[..^1];
        if (string.IsNullOrWhiteSpace(original)) throw InvalidRequest();
        if (request["chat_template_kwargs"] is not null and not JsonObject) throw InvalidRequest();
        var options = request["chat_template_kwargs"] as JsonObject ?? new JsonObject();
        if (!options.ContainsKey("target_lang")) options["target_lang"] = LanguageCode(target);
        if (!options.ContainsKey("source_lang") && !string.IsNullOrWhiteSpace(source)
            && !source.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && !source.Equals("auto-detected", StringComparison.OrdinalIgnoreCase))
            options["source_lang"] = LanguageCode(source);
        if (request["chat_template_kwargs"] is null) request["chat_template_kwargs"] = options;
        user!["content"] = original;
        var context = string.Join("\n", header.Split('\n').Where(line =>
            !line.TrimStart().StartsWith("Source language:", StringComparison.OrdinalIgnoreCase)
            && !line.TrimStart().StartsWith("Target language:", StringComparison.OrdinalIgnoreCase)
            && !line.Trim().Equals("Source text (untrusted):", StringComparison.OrdinalIgnoreCase))).Trim();
        if (context.Length > 0)
            messages.Insert(0, new JsonObject { ["role"] = "system", ["content"] = context });
        // A translation-only client needs a complete translation, not another PFML segment.
        if (!request.ContainsKey("stop")) request["stop"] = new JsonArray("<|plamo:tag|>", "<|plamo:bos|>");
    }

    private static string? ReadField(string header, string field)
    {
        string? result = null;
        using var reader = new StringReader(header);
        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf(':');
            if (separator < 0 || !line[..separator].Trim().Equals(field, StringComparison.OrdinalIgnoreCase)) continue;
            if (result is not null) throw InvalidRequest();
            result = line[(separator + 1)..].Trim();
            if (result.Length == 0) throw InvalidRequest();
        }
        return result;
    }

    private static string LanguageCode(string language)
    {
        if (Languages.TryGetValue(language, out var code)) return code;
        if (Codes.Contains(language)) return language.ToLowerInvariant();
        throw InvalidRequest();
    }

    private static InvalidDataException InvalidRequest() => new(Localization.T("engine.plamo.invalidRequest"));
}
