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
        Assert(await CheckAsync("v0.9.9") is null, "The installed release must not trigger an update.");
        Assert(await CheckAsync("v0.9.8") is null, "An older release must not trigger an update.");
        Assert(await CheckAsync("v0.10.0", draft: true) is null, "Draft releases must not trigger an update.");
        Assert(await CheckAsync("v0.10.0", prerelease: true) is null, "Prereleases must not trigger an update.");
        Assert(await CheckAsync("v0.10.0", executable: false) is null, "A release must have an uploaded Windows executable.");
        Assert(await CheckAsync("v0.10.0-rc.1") is null, "Only stable version tags may trigger an update.");

        async Task<ReleaseUpdate?> CheckAsync(string tag, bool draft = false, bool prerelease = false, bool executable = true)
        {
            using var client = new HttpClient(new ReleaseResponse(tag, draft, prerelease, executable));
            return await ReleaseUpdates.CheckAsync(installed, default, client);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ReleaseResponse(string tag, bool draft, bool prerelease, bool executable) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert(request.RequestUri?.AbsoluteUri == "https://api.github.com/repos/motionsilse/GemmaLauncher/releases/latest" &&
                   request.Headers.UserAgent.Count > 0 && request.Headers.Authorization is null,
                "The update check must use the official public releases endpoint.");
            var assets = executable ? new[] { new { name = $"CatLoader-{tag[1..]}-win-x64.exe", state = "uploaded" } } : [];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { tag_name = tag, draft, prerelease, assets })
            });
        }
    }
}
