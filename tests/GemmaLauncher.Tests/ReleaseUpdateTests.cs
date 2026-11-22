using System.Net;
using System.Net.Http.Json;
using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

internal static class ReleaseUpdateTests
{
    public static async Task RunAsync()
    {
        var installed = new Version(0, 9, 9, 0);
        var update = await CheckAsync("v0.10.0");
        Assert(update?.Version == new Version(0, 10, 0) &&
               update.DownloadPage.AbsoluteUri == "https://github.com/motionsilse/GemmaLauncher/releases/tag/v0.10.0",
            "A newer release must open the official download page.");
        Assert(update!.Package is { Size: 3 } package &&
            package.Sha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 })) &&
            package.DownloadUri.AbsoluteUri == "https://github.com/motionsilse/GemmaLauncher/releases/download/v0.10.0/CatLoader-0.10.0-win-x64.exe",
            "The official asset and digest must be available for installation inside the app.");
        Assert((await CheckAsync("v0.10.0", validDigest: false)) is { Package: null }, "An unverifiable release must still offer its download page.");
        Assert(await CheckAsync("v0.9.9") is null, "The installed release must not trigger an update.");
        Assert(await CheckAsync("v0.9.8") is null, "An older release must not trigger an update.");
        Assert(await CheckAsync("v0.10.0", draft: true) is null, "Draft releases must not trigger an update.");
        Assert(await CheckAsync("v0.10.0", prerelease: true) is null, "Prereleases must not trigger an update.");
        Assert(await CheckAsync("v0.10.0", executable: false) is null, "A release must have an uploaded Windows executable.");
        Assert(await CheckAsync("v0.10.0-rc.1") is null, "Only stable version tags may trigger an update.");
        await TestDownloadAndReplacementAsync(update);

        async Task<ReleaseUpdate?> CheckAsync(string tag, bool draft = false, bool prerelease = false, bool executable = true, bool validDigest = true)
        {
            using var client = new HttpClient(new ReleaseResponse(tag, draft, prerelease, executable, validDigest));
            return await ReleaseUpdates.CheckAsync(installed, default, client);
        }
    }

    private static async Task TestDownloadAndReplacementAsync(ReleaseUpdate update)
    {
        var folder = Directory.CreateTempSubdirectory("catloader-update-");
        try
        {
            var staged = Path.Combine(folder.FullName, "staged");
            var active = Path.Combine(folder.FullName, "CatLoader.exe");
            var old = new byte[] { 4, 5, 6 };
            var next = new byte[] { 1, 2, 3 };
            await File.WriteAllBytesAsync(active, old);
            await RejectAsync(new AssetResponse([1, 2, 4]), typeof(InvalidDataException));
            await RejectAsync(new AssetResponse(next, "https://example.com/asset"), typeof(HttpRequestException));

            using var client = new HttpClient(new AssetResponse(next, "https://release-assets.githubusercontent.com/asset"));
            await ReleaseUpdates.DownloadAsync(update, staged, null, default, client);
            Assert((await File.ReadAllBytesAsync(active)).SequenceEqual(old), "Downloading must leave the active executable untouched.");
            var installation = PortableUpdate.Replace(active, staged);
            Assert((await File.ReadAllBytesAsync(active)).SequenceEqual(next) && !File.Exists(staged), "Installation must replace the executable at its existing path.");
            installation.Restore();
            Assert((await File.ReadAllBytesAsync(active)).SequenceEqual(old), "Failed relaunch must be able to restore the old executable.");

            async Task RejectAsync(AssetResponse response, Type expected)
            {
                using var invalid = new HttpClient(response);
                try { await ReleaseUpdates.DownloadAsync(update, staged, null, default, invalid); }
                catch (Exception error) when (error.GetType() == expected)
                {
                    Assert(!File.Exists(staged) && (await File.ReadAllBytesAsync(active)).SequenceEqual(old),
                        "Failed verification must discard the staged file and preserve the existing executable.");
                    return;
                }
                throw new InvalidOperationException("An untrusted release download succeeded.");
            }
        }
        finally { folder.Delete(recursive: true); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class AssetResponse(byte[] bytes, string? redirect = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (redirect is not null && request.RequestUri!.Host == "github.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(redirect) } });
            Assert(request.RequestUri!.Host is "release-assets.githubusercontent.com" or "github.com", "Only GitHub may serve the asset.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }

    private sealed class ReleaseResponse(string tag, bool draft, bool prerelease, bool executable, bool validDigest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert(request.RequestUri?.AbsoluteUri == "https://api.github.com/repos/motionsilse/GemmaLauncher/releases/latest" &&
                   request.Headers.UserAgent.Count > 0 && request.Headers.Authorization is null,
                "The update check must use the official public releases endpoint.");
            var name = $"CatLoader-{tag[1..]}-win-x64.exe";
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 })).ToLowerInvariant();
            var assets = executable ? new[] { new { name, state = "uploaded", size = 3, digest = validDigest ? "sha256:" + digest : "unknown" } } : [];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { tag_name = tag, draft, prerelease, assets })
            });
        }
    }
}
