using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GemmaLauncher.Core;

public sealed record LanguageOption(string Code, string Name);

public sealed class Localization : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<LanguageOption> SupportedLanguages = Array.AsReadOnly<LanguageOption>([
        new("ko", "한국어"), new("en", "English"), new("ja", "日本語"),
        new("zh-cn", "简体中文"), new("zh-tw", "繁體中文"), new("es", "Español"),
        new("pt", "Português"), new("fr", "Français"), new("de", "Deutsch"),
        new("fil", "Filipino"), new("vi", "Tiếng Việt"), new("ru", "Русский"),
        new("pl", "Polski"), new("id", "Bahasa Indonesia"), new("ms", "Bahasa Melayu"),
        new("tr", "Türkçe"), new("th", "ไทย")
    ]);
    private readonly string _originalUiCulture;
    private readonly Func<string, IEnumerable<string>> _resourceProvider;
    private readonly Dictionary<string, string> _english;
    private readonly object _messageLock = new();
    private readonly Dictionary<string, MessageSource> _messages = new(StringComparer.Ordinal);
    private readonly Queue<string> _messageOrder = new();
    private const int MaximumRememberedMessages = 512;
    private LanguageState _state;

    public static Localization Current { get; } = new(CultureInfo.CurrentUICulture.Name, EmbeddedDocuments);
    public IReadOnlyList<LanguageOption> Languages => SupportedLanguages;
    public string LanguageCode => Volatile.Read(ref _state).Code;
    public string Selection => Volatile.Read(ref _state).Selection;
    public string this[string key] => Format(Volatile.Read(ref _state), key, []);
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    internal Localization(string originalUiCulture, Func<string, IEnumerable<string>> resourceProvider)
    {
        _originalUiCulture = originalUiCulture;
        _resourceProvider = resourceProvider;
        _english = ReadDocuments("en");
        _state = MakeState("auto");
    }

    public static string T(string key, params object[] args) => Current.Text(key, args);

    public string Retranslate(string message)
    {
        MessageSource? source;
        lock (_messageLock) _messages.TryGetValue(message, out source);
        if (source is null) return message;
        var translated = RenderSource(Volatile.Read(ref _state), source);
        if (translated.Length <= 4096)
        {
            lock (_messageLock)
            {
                if (!_messages.ContainsKey(translated)) _messageOrder.Enqueue(translated);
                _messages[translated] = source;
                while (_messageOrder.Count > MaximumRememberedMessages) _messages.Remove(_messageOrder.Dequeue());
            }
        }
        return translated;
    }

    public void SetLanguage(string selection)
    {
        var normalized = string.IsNullOrWhiteSpace(selection) || selection.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "auto" : ResolveLanguage(selection);
        if (Selection == normalized) return;
        Volatile.Write(ref _state, MakeState(normalized));
        PropertyChanged?.Invoke(this, new("Item[]"));
        PropertyChanged?.Invoke(this, new(nameof(LanguageCode)));
        PropertyChanged?.Invoke(this, new(nameof(Selection)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public static string ResolveLanguage(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return "en";
        var parts = languageTag.Trim().Replace('_', '-').ToLowerInvariant().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "en";
        if (parts[0] == "zh") return parts.Any(part => part is "hant" or "tw" or "hk" or "mo") ? "zh-tw" : "zh-cn";
        if (parts[0] == "tl") return "fil";
        return SupportedLanguages.Any(language => language.Code == parts[0]) ? parts[0] : "en";
    }

    internal string Text(string key, params object[] args)
    {
        var state = Volatile.Read(ref _state);
        var text = Format(state, key, args);
        if (text.Length <= 4096 && args.Length <= 16 && state.Strings.ContainsKey(key))
        {
            lock (_messageLock)
            {
                var captured = args.Select(argument => argument is string value && _messages.TryGetValue(value, out var nested) && nested.Depth < 4
                    ? (object)nested : argument).ToArray();
                var depth = captured.OfType<MessageSource>().Select(source => source.Depth + 1).DefaultIfEmpty(1).Max();
                if (!_messages.ContainsKey(text)) _messageOrder.Enqueue(text);
                _messages[text] = new(key, captured, depth);
                while (_messageOrder.Count > MaximumRememberedMessages) _messages.Remove(_messageOrder.Dequeue());
            }
        }
        return text;
    }

    private string RenderSource(LanguageState state, MessageSource source)
    {
        var args = source.Arguments.Select(argument => argument is MessageSource nested ? RenderSource(state, nested) : argument).ToArray();
        return Format(state, source.Key, args);
    }

    private string Format(LanguageState state, string key, object[] args)
    {
        if (string.IsNullOrEmpty(key)) return key ?? "";
        var text = state.Strings.GetValueOrDefault(key, key);
        if (args.Length == 0) return text;
        try { return string.Format(state.Culture, text, args); }
        catch (FormatException)
        {
            var fallback = _english.GetValueOrDefault(key, key);
            try { return string.Format(state.Culture, fallback, args); }
            catch (FormatException) { return fallback; }
        }
    }

    private LanguageState MakeState(string selection)
    {
        var code = ResolveLanguage(selection == "auto" ? _originalUiCulture : selection);
        var strings = new Dictionary<string, string>(_english, StringComparer.Ordinal);
        if (code != "en")
        {
            foreach (var (key, value) in ReadDocuments(code))
            {
                try
                {
                    var arguments = CompositeFormat.Parse(value).MinimumArgumentCount;
                    if (_english.TryGetValue(key, out var english) && CompositeFormat.Parse(english).MinimumArgumentCount != arguments) continue;
                    strings[key] = value;
                }
                catch (FormatException) { /* Keep the English entry when a translation has broken placeholders. */ }
            }
        }
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
        return new(code, selection, culture, strings);
    }

    private Dictionary<string, string> ReadDocuments(string code)
    {
        Dictionary<string, string> entries = new(StringComparer.Ordinal);
        try
        {
            foreach (var json in _resourceProvider(code))
            {
                try
                {
                    using var document = JsonDocument.Parse(json);
                    if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                    foreach (var entry in document.RootElement.EnumerateObject())
                        if (entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { } text && !string.IsNullOrWhiteSpace(text))
                            entries[entry.Name] = text;
                }
                catch (JsonException) { /* One damaged domain must not prevent the application from opening. */ }
            }
        }
        catch (IOException) { }
        return entries;
    }

    private static IEnumerable<string> EmbeddedDocuments(string code)
    {
        var assembly = typeof(Localization).Assembly;
        foreach (var domain in new[] { "engine", "ui", "models" })
        {
            using var stream = assembly.GetManifestResourceStream($"GemmaLauncher.Core.Locales.{code}.{domain}.json");
            if (stream is null) continue;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            yield return reader.ReadToEnd();
        }
    }

    private sealed record LanguageState(string Code, string Selection, CultureInfo Culture, Dictionary<string, string> Strings);
    private sealed record MessageSource(string Key, object[] Arguments, int Depth);
}
