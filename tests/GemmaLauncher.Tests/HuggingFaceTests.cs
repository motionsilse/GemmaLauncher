using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

public static class HuggingFaceTests
{
    public static async Task RunAsync()
    {
        foreach (var input in new[] { "https://huggingface.co.example.org/user/model", "https://huggingface.co/user/model/tree/main", "file:///tmp/model", "user", "user/../model" })
        {
            try { HuggingFaceModels.ParseRepository(input); throw new Exception("Invalid repository accepted: " + input); }
            catch (InvalidDataException) { }
        }
        var file = Path.Combine(Path.GetTempPath(), "GemmaLauncher-hf-" + Guid.NewGuid().ToString("N") + ".gguf");
        LibraryTests.WriteGguf(file);
        var bytes = await File.ReadAllBytesAsync(file);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        const string revision = "0123456789abcdef0123456789abcdef01234567";
        var rangeSeen = false;
        using var client = new HttpClient(new Handler(request =>
        {
            HttpResponseMessage response;
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal))
            {
                response = new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { sha = revision, siblings = new[] { new { rfilename = "quant/model-Q4_K_M.gguf", lfs = new { sha256 = hash, size = bytes.Length } } } })) };
            }
            else
            {
                if (!request.RequestUri.AbsolutePath.Contains("/resolve/" + revision + "/", StringComparison.Ordinal)) throw new Exception("Download URL was not pinned.");
                rangeSeen = request.Headers.Range?.Ranges.Single().From == 0;
                response = new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, bytes.Length - 1, bytes.Length);
            }
            response.RequestMessage = request; return response;
        }));
        using var hub = new HuggingFaceModels(client);
        var files = await hub.ListAsync("https://huggingface.co/user/model");
        var model = await hub.InspectAsync(files.Single());
        if (!rangeSeen || model.Artifacts[0].Sha256 != hash || model.Origin != "huggingface" || model.Profile.Lazy != "off") throw new Exception("HF imports must use verified metadata and generic defaults.");
        using var noRange = new HttpClient(new Handler(request => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request }));
        using var invalidHub = new HuggingFaceModels(noRange);
        try { await invalidHub.InspectAsync(files.Single()); throw new Exception("A full response to a header probe must be rejected."); }
        catch (InvalidDataException) { }
    }

    public static async Task SmokeAsync()
    {
        using var hub = new HuggingFaceModels();
        foreach (var repository in new[] { "unsloth/Hy-MT2-1.8B-GGUF", "unsloth/Hy-MT2-7B-GGUF" })
        {
            var files = await hub.ListAsync(repository);
            var file = files.Single(item => item.Filename.EndsWith("-Q4_K_M.gguf", StringComparison.Ordinal));
            var model = await hub.InspectAsync(file);
            Console.WriteLine($"{model.DisplayName}: {model.Architecture}, {file.Bytes} bytes, header inspection passed.");
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
