using System.Text;
using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

public static class LibraryTests
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "GemmaLauncher-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "my model.gguf");
        WriteGguf(file);
        var metadata = GgufMetadata.Read(file);
        Assert(metadata.Architecture == "llama" && metadata.ContextLength == 32768, "Read actual model architecture and context.");
        var gemma = metadata with { Architecture = "gemma4", EmbeddingLength = 2560 };
        var assistant = metadata with { Architecture = "gemma4-assistant", EmbeddingLength = 256, EmbeddingOutputLength = 2560 };
        GgufMetadata.RequireMtpCompatible(gemma, assistant);
        try { GgufMetadata.RequireMtpCompatible(gemma, assistant with { EmbeddingOutputLength = 2048 }); throw new Exception("MTP must match target output dimensions."); }
        catch (InvalidDataException) { }
        try { GgufMetadata.RequireMtpCompatible(gemma, assistant with { VocabularyFingerprint = "different" }); throw new Exception("MTP must match vocabulary."); }
        catch (InvalidDataException) { }
        var imported = await ModelImportService.ImportLocalAsync(file, []);
        Assert(imported.Definition.Profile.Lazy == "off" && imported.Definition.Profile.MtpDraftMax == 0, "A user model must not inherit Gemma optimizations.");
        Assert(imported.Files.Values.Single() == file && File.Exists(file), "Import must link the original file without moving it.");
        var repeated = await ModelImportService.ImportLocalAsync(file, [imported.Definition]);
        Assert(repeated.Definition.Id == imported.Definition.Id, "Repeated imports must share the same model identity.");
        var paths = new LauncherPaths(Path.Combine(root, "app"));
        using var installer = new InstallationService(paths, modelFiles: imported.Files);
        var installed = await installer.EnsureModelAsync(imported.Definition);
        Assert(installed.ModelPath == file && installed.MtpPath is null, "Local installation must not attempt a download.");
        File.Move(file, file + ".moved");
        try { await installer.EnsureModelAsync(imported.Definition); throw new Exception("Missing local model should fail."); }
        catch (FileNotFoundException) { }
        WriteGguf(file, "not-an-engine-architecture");
        try { await ModelImportService.ImportLocalAsync(file, []); throw new Exception("Unsupported architecture should fail."); }
        catch (InvalidDataException) { }
        File.WriteAllText(file, "not a GGUF file");
        try { GgufMetadata.Read(file); throw new Exception("Invalid magic should fail."); }
        catch (InvalidDataException) { }
        WriteGguf(file, splitCount: 2);
        try { await ModelImportService.ImportLocalAsync(file, []); throw new Exception("A lone split model must not be treated as a complete model."); }
        catch (InvalidDataException) { }
        WriteGguf(file);
        using (var stream = File.OpenWrite(file)) stream.SetLength(30);
        try { GgufMetadata.Read(file); throw new Exception("Truncated metadata should fail."); }
        catch (InvalidDataException) { }
        var invalid = new ModelLoadOptions { GpuLayers = "--port 9000" };
        try { invalid.Validate(); throw new Exception("Load options must be typed, not shell fragments."); }
        catch (InvalidDataException) { }
    }

    public static void WriteGguf(string path, string architecture = "llama", ushort splitCount = 1)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(0x46554747u); writer.Write(3u); writer.Write(1UL); writer.Write(7UL);
        Text("general.architecture"); writer.Write(8u); Text(architecture);
        Text("general.name"); writer.Write(8u); Text("My local model");
        Text(architecture + ".context_length"); writer.Write(4u); writer.Write(32768u);
        Text(architecture + ".embedding_length"); writer.Write(4u); writer.Write(4096u);
        Text("tokenizer.chat_template"); writer.Write(8u); Text("{% for message in messages %}{{ message.content }}{% endfor %}");
        Text("tokenizer.ggml.tokens"); writer.Write(9u); writer.Write(8u); writer.Write(2UL); Text("a"); Text("b");
        Text("split.count"); writer.Write(2u); writer.Write(splitCount);
        Text("token_embd.weight"); writer.Write(2u); writer.Write(2UL); writer.Write(2UL); writer.Write(0u); writer.Write(0UL);
        while (stream.Position % 32 != 0) writer.Write((byte)0);
        writer.Write(new byte[16]);
        void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write((ulong)bytes.Length); writer.Write(bytes); }
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
