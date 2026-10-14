using System.Security.Cryptography;
using System.Text;

namespace GemmaLauncher.Core;

/// <summary>Reads bounded GGUF metadata; model tensors are never loaded into RAM by the launcher.</summary>
public sealed record GgufMetadata(string Architecture, string Name, int ContextLength, int EmbeddingLength,
    int BlockCount, string ChatTemplate, string VocabularyFingerprint, int SplitCount, int EmbeddingOutputLength = 0)
{
    private const long MetadataLimit = 128 * 1024 * 1024;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly HashSet<string> Supported = LoadArchitectures();
    public void RequireLoadable()
    {
        if (!Supported.Contains(Architecture) || Architecture is "clip" or "bert" or "nomic-bert" or "nomic-bert-moe" or "gemma4-assistant")
            throw new InvalidDataException(Localization.T("engine.library.architecture", Architecture));
        if (SplitCount > 1) throw new InvalidDataException(Localization.T("engine.library.split"));
    }

    public static GgufMetadata Read(string path, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream, cancellationToken);
    }

    public static GgufMetadata Read(Stream stream, CancellationToken cancellationToken = default, long? modelBytes = null)
    {
        if (!stream.CanSeek) throw new ArgumentException("A seekable stream is required.", nameof(stream));
        using var reader = new BinaryReader(stream, StrictUtf8, leaveOpen: true);
        using var vocabulary = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var scalars = new Dictionary<string, object>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var hasVocabulary = false;
        try
        {
            if (reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() is not (2 or 3)) throw Invalid();
            var tensors = reader.ReadUInt64(); var count = reader.ReadUInt64();
            if (tensors is 0 or > 1_000_000 || count > 100_000) throw Invalid();
            for (ulong index = 0; index < count; index++)
            {
                var key = Text(1024); var type = reader.ReadUInt32();
                if (!keys.Add(key)) throw Invalid();
                if (type == 9)
                {
                    var itemType = reader.ReadUInt32(); var items = reader.ReadUInt64();
                    if (items > 2_000_000 || itemType == 9) throw Invalid();
                    var tokens = key == "tokenizer.ggml.tokens";
                    for (ulong item = 0; item < items; item++)
                    {
                        var value = Value(itemType);
                        if (tokens)
                        {
                            if (value is not string token) throw Invalid();
                            var bytes = Encoding.UTF8.GetBytes(token);
                            vocabulary.AppendData(BitConverter.GetBytes(bytes.Length)); vocabulary.AppendData(bytes);
                        }
                    }
                    if (tokens) hasVocabulary = true;
                }
                else if (!scalars.TryAdd(key, Value(type))) throw Invalid();
                Check(0);
            }
            // Validate tensor descriptors and the start of their data without reading weights.
            ulong maxOffset = 0;
            for (ulong index = 0; index < tensors; index++)
            {
                Text(1024); var dimensions = reader.ReadUInt32();
                if (dimensions is 0 or > 4) throw Invalid();
                for (var dimension = 0; dimension < dimensions; dimension++)
                    if (reader.ReadUInt64() is 0 or > 1_000_000_000) throw Invalid();
                reader.ReadUInt32(); maxOffset = Math.Max(maxOffset, reader.ReadUInt64()); Check(0);
            }
            var alignment = Number("general.alignment", 32);
            if (alignment is < 1 or > 4096 || (alignment & (alignment - 1)) != 0) throw Invalid();
            var dataStart = (stream.Position + alignment - 1) / alignment * alignment;
            var fullLength = modelBytes ?? stream.Length;
            if (dataStart >= fullLength || maxOffset >= (ulong)(fullLength - dataStart)) throw Invalid();
            var architecture = String("general.architecture");
            if (string.IsNullOrWhiteSpace(architecture)) throw Invalid();
            return new(architecture, String("general.name"), Number(architecture + ".context_length", 8192),
                Number(architecture + ".embedding_length", 0), Number(architecture + ".block_count", 0),
                String("tokenizer.chat_template"), hasVocabulary ? Convert.ToHexString(vocabulary.GetHashAndReset()) : "", Number("split.count", 1), Number(architecture + ".embedding_length_out", 0));
        }
        catch (Exception ex) when (ex is EndOfStreamException or OverflowException or DecoderFallbackException)
        { throw new InvalidDataException(Localization.T("engine.library.invalidGguf"), ex); }

        object Value(uint type)
        {
            Check(0);
            return type switch
            {
                0 => reader.ReadByte(), 1 => reader.ReadSByte(), 2 => reader.ReadUInt16(), 3 => reader.ReadInt16(),
                4 => reader.ReadUInt32(), 5 => reader.ReadInt32(), 6 => reader.ReadSingle(), 7 => reader.ReadBoolean(),
                8 => Text(1_000_000), 10 => reader.ReadUInt64(), 11 => reader.ReadInt64(), 12 => reader.ReadDouble(), _ => throw Invalid()
            };
        }
        string Text(int max)
        {
            Check(8); var length = reader.ReadUInt64(); if (length > (ulong)max) throw Invalid();
            Check((long)length); var bytes = reader.ReadBytes((int)length);
            if (bytes.Length != (int)length) throw Invalid();
            return StrictUtf8.GetString(bytes);
        }
        void Check(long bytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stream.Position + bytes > MetadataLimit || stream.Position + bytes > stream.Length) throw Invalid();
        }
        string String(string key) => scalars.GetValueOrDefault(key) as string ?? "";
        int Number(string key, int fallback)
        {
            if (!scalars.TryGetValue(key, out var value)) return fallback;
            if (value is not (byte or sbyte or short or ushort or int or uint or long or ulong)) throw Invalid();
            var number = Convert.ToInt64(value); if (number is < 0 or > int.MaxValue) throw Invalid(); return (int)number;
        }
        static InvalidDataException Invalid() => new(Localization.T("engine.library.invalidGguf"));
    }

    public static void RequireMtpCompatible(GgufMetadata model, GgufMetadata companion)
    {
        if (model.Architecture != "gemma4" || companion.Architecture != "gemma4-assistant" ||
            model.EmbeddingLength == 0 || model.EmbeddingLength != companion.EmbeddingOutputLength ||
            model.VocabularyFingerprint.Length == 0 || model.VocabularyFingerprint != companion.VocabularyFingerprint)
            throw new InvalidDataException(Localization.T("engine.library.incompatibleMtp"));
    }

    private static HashSet<string> LoadArchitectures()
    {
        using var stream = typeof(GgufMetadata).Assembly.GetManifestResourceStream("GemmaLauncher.Architectures.txt")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }
}
