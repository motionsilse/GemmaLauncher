using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using Microsoft.VisualBasic.FileIO;

namespace GemmaLauncher.Core;

public sealed record GlossaryEntry(string Source, string Target);

public sealed record TaskPreset
{
    private static readonly JsonSerializerOptions DictionaryJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Prompt { get; init; } = "";
    public GlossaryEntry[] Glossary { get; init; } = [];
    public double? Temperature { get; init; }
    public double? TopP { get; init; }
    public int? TopK { get; init; }
    public int? MaxTokens { get; init; }
    public static TaskPreset Empty { get; } = new() { Id = "none", Name = "Model defaults" };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Name.Any(char.IsControl) || Prompt is null || Prompt.Length > 100_000 ||
            Glossary is null || Glossary.Length > 2000 || Glossary.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Source) ||
                string.IsNullOrWhiteSpace(entry.Target) || entry.Source.Length > 500 || entry.Target.Length > 500) ||
            Temperature is double t && (!double.IsFinite(t) || t is < 0 or > 2) ||
            TopP is double p && (!double.IsFinite(p) || p is <= 0 or > 1) || TopK is < 0 or > 1000 || MaxTokens is < 1 or > 131072)
            throw new InvalidDataException(Localization.T("engine.library.invalidPreset"));
    }

    public string Instructions => string.Join("\n\n", new[]
    {
        Prompt.Trim(),
        Glossary.Length == 0 ? "" : "Terminology dictionary (source → target):\n" + JsonSerializer.Serialize(Glossary, DictionaryJson)
    }.Where(value => value.Length > 0));

    public JsonObject Apply(JsonObject request, bool chat, bool nativeCompletion = false)
    {
        Validate();
        var result = (JsonObject)request.DeepClone();
        var instructions = Instructions;
        if (instructions.Length > 0)
        {
            var prefix = "[Task instructions]\n" + instructions + "\n[/Task instructions]\n\n";
            if (chat)
            {
                if (result["messages"] is not JsonArray messages) throw InvalidRequest();
                var user = messages.OfType<JsonObject>().LastOrDefault(message => message["role"]?.ToString() == "user") ?? throw InvalidRequest();
                if (user["content"] is JsonValue text && text.TryGetValue<string>(out var content)) user["content"] = prefix + content;
                else if (user["content"] is JsonArray parts) parts.Insert(0, new JsonObject { ["type"] = "text", ["text"] = prefix });
                else throw InvalidRequest();
            }
            else if (result["prompt"] is JsonValue prompt && prompt.TryGetValue<string>(out var text)) result["prompt"] = prefix + text;
            else if (result["prompt"] is JsonArray prompts && prompts.Count > 0 && prompts.All(item => item is JsonValue value && value.TryGetValue<string>(out _)))
                for (var i = 0; i < prompts.Count; i++) prompts[i] = prefix + prompts[i]!.GetValue<string>();
            else throw InvalidRequest();
        }
        AddDefault("temperature", Temperature); AddDefault("top_p", TopP); AddDefault("top_k", TopK);
        if (MaxTokens is int maximum)
        {
            if (!result.ContainsKey("max_tokens") && !result.ContainsKey("max_completion_tokens") && !result.ContainsKey("n_predict"))
                result[nativeCompletion ? "n_predict" : "max_tokens"] = maximum;
        }
        return result;
        void AddDefault<T>(string key, T? value) where T : struct { if (!result.ContainsKey(key) && value.HasValue) result[key] = JsonValue.Create(value.Value); }
        static InvalidDataException InvalidRequest() => new(Localization.T("engine.library.presetRequest"));
    }

    public static GlossaryEntry[] ReadGlossary(string text)
    {
        if (text.Length > 2_000_000) throw new InvalidDataException(Localization.T("engine.library.invalidPreset"));
        using var parser = new TextFieldParser(new StringReader(text)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(text.Contains('\t') ? "\t" : ",");
        var entries = new List<GlossaryEntry>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is not { Length: 2 } || fields.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException(Localization.T("engine.library.glossaryFormat"));
            if (entries.Count == 0 && fields[0].Equals("source", StringComparison.OrdinalIgnoreCase) && fields[1].Equals("target", StringComparison.OrdinalIgnoreCase)) continue;
            entries.Add(new(fields[0], fields[1]));
            if (entries.Count > 2000) throw new InvalidDataException(Localization.T("engine.library.invalidPreset"));
        }
        var preset = Empty with { Glossary = entries.ToArray() }; preset.Validate(); return preset.Glossary;
    }
}
