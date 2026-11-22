using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace GemmaLauncher.Core;

public sealed record ReleasePackage(Uri DownloadUri, long Size, string Sha256);
public sealed record ReleaseUpdate(Version Version, Uri DownloadPage, ReleasePackage? Package = null);

public static class ReleaseUpdates
{
    private const string Repository = "motionsilse/GemmaLauncher";
    private const long MaxPackageBytes = 128 * 1024 * 1024;
    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(8),
        MaxResponseContentBufferSize = 512 * 1024
    };
    private static readonly HttpClient Downloads = new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false });

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

        var executable = ExecutableName(latest);
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object || ReadString(asset, "name") != executable || ReadString(asset, "state") != "uploaded") continue;
            ReleasePackage? package = null;
            if (asset.TryGetProperty("size", out var sizeField) && sizeField.ValueKind == JsonValueKind.Number &&
                sizeField.TryGetInt64(out var size) && size is > 0 and <= MaxPackageBytes &&
                ReadString(asset, "digest") is { Length: 71 } digest && digest.StartsWith("sha256:", StringComparison.Ordinal) &&
                digest.AsSpan(7).ToString().All(Uri.IsHexDigit))
                package = new(PackageUri(latest), size, digest[7..].ToUpperInvariant());
            return new(latest, new Uri($"https://github.com/{Repository}/releases/tag/{tag}"), package);
        }
        return null;
    }

    public static async Task DownloadAsync(ReleaseUpdate release, string destination, IProgress<int>? progress, CancellationToken token, HttpClient? client = null)
    {
        var file = release.Package;
        if (file is null || file.DownloadUri != PackageUri(release.Version) || file.Size is <= 0 or > MaxPackageBytes ||
            file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException(Localization.T("engine.install.verificationRequired"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var url = file.DownloadUri;
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("CatLoader");
            request.Headers.AcceptEncoding.ParseAdd("identity");
            using var response = await (client ?? Downloads).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { IsAbsoluteUri: true } target &&
                target.Scheme == Uri.UriSchemeHttps && target.IsDefaultPort && target.UserInfo.Length == 0 &&
                target.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com")
            {
                url = target;
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentLength is long size && size != file.Size ||
                response.Content.Headers.ContentEncoding.Any(value => !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(Localization.T("engine.install.sizeMismatch"));

            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            InstallationService.RejectReparsePoints(destination);
            var created = false;
            try
            {
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                created = true;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long received = 0;
                var lastPercent = -1;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                {
                    received += read;
                    if (received > file.Size) throw new InvalidDataException(Localization.T("engine.install.sizeExceeded"));
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    var percent = (int)(received * 100 / file.Size);
                    if (percent != lastPercent) progress?.Report(lastPercent = percent);
                }
                if (received != file.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Localization.T("engine.install.verificationFailed"));
                return;
            }
            catch
            {
                if (created) File.Delete(destination);
                throw;
            }
        }
        throw new HttpRequestException(Localization.T("engine.install.insecureConnection"));
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static string ExecutableName(Version version) => $"CatLoader-{version.ToString(3)}-win-x64.exe";
    private static Uri PackageUri(Version version) => new($"https://github.com/{Repository}/releases/download/v{version.ToString(3)}/{ExecutableName(version)}");
}
