using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GemmaLauncher.Core;

public sealed record HuggingFaceFile(string Repository, string Revision, string Path, long Bytes, string Sha256)
{
    public string Filename => System.IO.Path.GetFileName(Path);
    public string DisplayName => $"{Path} · {Bytes / 1_000_000_000d:0.00} GB";
    public string SourceUrl => $"https://huggingface.co/{Repository}";
    public string DownloadUrl => $"{SourceUrl}/resolve/{Revision}/" + string.Join('/', Path.Split('/').Select(Uri.EscapeDataString));
}

public sealed partial class HuggingFaceModels : IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    public HuggingFaceModels(HttpClient? client = null)
    {
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _ownsClient = client is null;
    }

    public static string ParseRepository(string input)
    {
        input = input.Trim().TrimEnd('/');
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != "https" || uri.Host != "huggingface.co" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) throw Invalid();
            input = uri.AbsolutePath.Trim('/');
        }
        if (!RepositoryPattern().IsMatch(input) || input.Split('/').Any(part => part is "." or "..")) throw Invalid();
        return input;
        static InvalidDataException Invalid() => new(Localization.T("engine.library.hfUrl"));
    }

    public async Task<HuggingFaceFile[]> ListAsync(string input, CancellationToken token = default)
    {
        var repository = ParseRepository(input);
        using var response = await _client.GetAsync($"https://huggingface.co/api/models/{repository}?blobs=true", HttpCompletionOption.ResponseHeadersRead, token);
        RequireSuccess(response);
        using var bytes = await ReadBoundedAsync(response.Content, 8 * 1024 * 1024, token);
        using var json = await JsonDocument.ParseAsync(bytes, cancellationToken: token);
        var revision = json.RootElement.GetProperty("sha").GetString() ?? "";
        if (!RevisionPattern().IsMatch(revision)) throw new InvalidDataException(Localization.T("engine.library.hfMetadata"));
        List<HuggingFaceFile> files = [];
        foreach (var sibling in json.RootElement.GetProperty("siblings").EnumerateArray())
        {
            var path = sibling.GetProperty("rfilename").GetString() ?? "";
            if (!path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) || !sibling.TryGetProperty("lfs", out var lfs)) continue;
            if (path.Split('/').Any(part => !CatalogLoader.SafeFilename(part))) continue;
            var hash = lfs.GetProperty("sha256").GetString() ?? "";
            var size = lfs.GetProperty("size").GetInt64();
            if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)) || size is <= 0 or > 1_000_000_000_000) continue;
            files.Add(new(repository, revision, path, size, hash));
        }
        if (files.Count == 0) throw new InvalidDataException(Localization.T("engine.library.hfNoFiles"));
        return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<ModelDefinition> InspectAsync(HuggingFaceFile file, CancellationToken token = default)
    {
        const int headerLimit = 16 * 1024 * 1024;
        using var request = new HttpRequestMessage(HttpMethod.Get, file.DownloadUrl);
        var last = Math.Min(file.Bytes, headerLimit) - 1;
        request.Headers.Range = new RangeHeaderValue(0, last);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        RequireSuccess(response);
        var range = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != 0 || range.To != last || range.Length != file.Bytes)
            throw new InvalidDataException(Localization.T("engine.library.hfRange"));
        using var header = await ReadBoundedAsync(response.Content, headerLimit, token);
        var metadata = await Task.Run(() => GgufMetadata.Read(header, token, file.Bytes), token);
        var artifact = new ModelArtifact { Role = "model", Filename = file.Filename, Url = file.DownloadUrl, Sha256 = file.Sha256, Bytes = file.Bytes };
        return ModelImportService.CreateDefinition(metadata, artifact, "huggingface", file.SourceUrl, file.SourceUrl + "/blob/" + file.Revision + "/README.md");
    }

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException(Localization.T("engine.install.insecureConnection"));
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidDataException(Localization.T("engine.library.hfAccess"));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<MemoryStream> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken token)
    {
        var output = new MemoryStream();
        try
        {
            await using var source = await content.ReadAsStreamAsync(token);
            var buffer = new byte[65536]; int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + read > maximum) throw new InvalidDataException(Localization.T("engine.library.hfMetadata"));
                output.Write(buffer, 0, read);
            }
            output.Position = 0; return output;
        }
        catch { output.Dispose(); throw; }
    }

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,99}/[a-zA-Z0-9][a-zA-Z0-9_.-]{0,149}$")]
    private static partial Regex RepositoryPattern();
    [GeneratedRegex("^[a-fA-F0-9]{40}$")]
    private static partial Regex RevisionPattern();
}
