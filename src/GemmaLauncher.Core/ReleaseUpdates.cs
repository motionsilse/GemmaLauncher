using System.Text.Json;

namespace GemmaLauncher.Core;

public sealed record ReleaseUpdate(Version Version, Uri DownloadPage);

public static class ReleaseUpdates
{
    private const string Repository = "motionsilse/GemmaLauncher";
    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(8),
        MaxResponseContentBufferSize = 512 * 1024
    };

    public static async Task<ReleaseUpdate?> CheckAsync(Version installed, CancellationToken token, HttpClient? client = null)
    {
        var current = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("CatLoader/" + current);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await (client ?? Client).SendAsync(request, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(content, cancellationToken: token);
        var release = json.RootElement;
        if (release.ValueKind != JsonValueKind.Object ||
            !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False ||
            !release.TryGetProperty("tag_name", out var tagField) || tagField.ValueKind != JsonValueKind.String ||
            tagField.GetString() is not { } tag || !tag.StartsWith('v') ||
            !Version.TryParse(tag.AsSpan(1), out var latest) || latest.Build < 0 || latest.Revision >= 0 ||
            tag != "v" + latest.ToString(3) || latest <= current)
            return null;

        var executable = $"CatLoader-{latest.ToString(3)}-win-x64.exe";
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array ||
            !assets.EnumerateArray().Any(asset => asset.ValueKind == JsonValueKind.Object &&
                asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == executable &&
                asset.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String && state.GetString() == "uploaded"))
            return null;

        return new(latest, new Uri($"https://github.com/{Repository}/releases/tag/{tag}"));
    }
}
