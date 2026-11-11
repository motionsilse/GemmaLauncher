using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using GemmaLauncher.Core;
using Microsoft.AspNetCore.WebUtilities;

namespace GemmaLauncher.Tests;

public static class AnisTests
{
    private const string OauthCredential = "fixture.header.signature";
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    public static async Task RunAsync()
    {
        foreach (var value in new[] { OauthCredential, "hf_oauth_fixture", "a-b.c_d~e+f/g==" })
            Assert(HuggingFaceAuthorization.IsValidAccessToken(value), "Valid OAuth bearer syntax must be accepted.");
        foreach (var value in new[] { "", "=", "=a", "a=b", "a b", "a\r\nInjected: value", "a\t", "한글", new string('a', 4097) })
            Assert(!HuggingFaceAuthorization.IsValidAccessToken(value), "Malformed bearer syntax must be rejected.");
        var catalog = CatalogLoader.Load(Path.Combine(Root, "src/GemmaLauncher.App/Assets/catalog.json"));
        var model = catalog.Models.Single(model => model.Id == "anis-trans");
        var root = new LauncherPaths(Path.Combine(Root, "artifacts/tests/anis-" + Guid.NewGuid().ToString("N")));
        var requests = new List<(Uri Uri, string? Token)>();
        using var client = new HttpClient(new ResponseHandler((request, token) =>
        {
            requests.Add((request.RequestUri!, request.Headers.Authorization?.Parameter));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }));
        using var backend = new AnisBackend(root, client: client);
        Assert(await backend.FindAsync(model) is null && requests.Count == 0, "An empty installation must remain discoverable without silently downloading.");
        foreach (var credential in new string?[] { null, "fixture-secret" })
        {
            try { await backend.EnsureModelAsync(model, credential, null); throw new Exception("Gated access was accepted without permission."); }
            catch (AnisAccessRequiredException ex) { Assert(!ex.ToString().Contains("fixture-secret"), "Access errors must not disclose credentials."); }
        }
        Assert(requests.Count == 2 && requests[0].Token is null && requests[1].Token == "fixture-secret", "A first download must support anonymous access followed by an explicit temporary credential.");
        Assert(requests.All(r => r.Uri.Host == "huggingface.co" && !r.Uri.Query.Contains("fixture-secret")), "Credentials belong only in authorization headers for official model URLs.");
        Assert(!Directory.EnumerateFiles(root.Root, "*.partial", SearchOption.AllDirectories).Any(), "A rejected download must leave no partial file.");
        var downloads = Path.Combine(root.Root, "downloads");
        Directory.CreateDirectory(downloads);
        var wrongConfig = Path.Combine(downloads, "config (1).json");
        await File.WriteAllBytesAsync(wrongConfig, new byte[224]);
        var missing = await backend.ImportBrowserDownloadsAsync(downloads);
        Assert(missing.Count == 4 && missing.Any(file => file.Path == "ct2/config.json"), "The browser importer must reject a correct-size file with an invalid hash.");
        Assert(File.Exists(wrongConfig) && !File.Exists(Path.Combine(backend.ManagedModelDirectory, "ct2", "config.json")), "Browser import must leave original files intact and publish only verified bytes.");
        await TestHuggingFaceLoginAsync();
        await TestDownloadRedirectsAsync(root);

        using var invalidClient = new HttpClient(new ResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) })));
        using var invalidBackend = new AnisBackend(new LauncherPaths(root.Root + "-invalid"), client: invalidClient);
        try { await invalidBackend.EnsureModelAsync(model, null, null); throw new Exception("Malformed model data was accepted."); }
        catch (InvalidDataException) { }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await AssertCancelledAsync(() => backend.EnsureModelAsync(model, null, null, cancelled.Token));

        var configuration = new LaunchConfiguration(new(model, Path.Combine(root.Root, "model", "ct2", "model.bin"), null), "python.exe", 1024, root.Logs)
        { BackendSourceDirectory = Path.Combine(root.Root, "source", "src"), Preset = new() { Name = "Ignored", Prompt = "Do not inject into ANIS" } };
        var args = ServerSession.BuildArguments(configuration);
        Assert(args.Contains("anis_trans.server") == false && args.Any(a => a.Contains("run_module('anis_trans.server'")), "ANIS must launch the official API module.");
        Assert(args.Contains("-I") && args.Contains("utf8") && args.Contains("127.0.0.1") && !args.Contains("--jinja") && !args.Contains("--ctx-size") && !args.Contains("Do not inject into ANIS"), "ANIS must use isolated UTF8 Python without incompatible llama.cpp options or preset text.");
        foreach (var invalid in new[] { configuration with { AnisDevice = "vulkan" }, configuration with { BackendSourceDirectory = null }, configuration with { Port = 0 } })
        {
            try { ServerSession.BuildArguments(invalid); throw new Exception("Invalid ANIS launch settings were accepted."); }
            catch (ArgumentException) { }
        }
    }
    private static async Task TestDownloadRedirectsAsync(LauncherPaths root)
    {
        var requests = new List<(string Host, string? Credential)>();
        using var client = new HttpClient(new ResponseHandler((request, _) =>
        {
            requests.Add((request.RequestUri!.Host, request.Headers.Authorization?.Parameter));
            return Task.FromResult(requests.Count switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("/api/resolve-cache/fixture", UriKind.Relative) } },
                2 => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://cdn.example/fixture") } },
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }
            });
        }));
        using var backend = new AnisBackend(new LauncherPaths(root.Root + "-redirect"), client: client);
        var model = CatalogLoader.Load(Path.Combine(Root, "src/GemmaLauncher.App/Assets/catalog.json")).Models.Single(m => m.Engine == "anis");
        try { await backend.EnsureModelAsync(model, OauthCredential, null); throw new Exception("Invalid redirected bytes were accepted."); }
        catch (InvalidDataException) { }
        Assert(requests.SequenceEqual(new (string, string?)[] { ("huggingface.co", OauthCredential), ("huggingface.co", OauthCredential), ("cdn.example", null) }),
            "Download authorization must stay on the Hugging Face origin and survive its same-origin redirect.");
    }
    private static async Task TestHuggingFaceLoginAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var browser = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues>? request = null;
        using var handler = new ResponseHandler(async (message, token) =>
        {
            Assert(message.RequestUri?.AbsoluteUri == "https://huggingface.co/oauth/token", "The callback code must go to the official token endpoint.");
            var form = await message.Content!.ReadAsStringAsync(token);
            request = QueryHelpers.ParseQuery(form);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = OauthCredential }) };
        });
        using var login = new HuggingFaceAuthorization("fixture-public-client", handler);
        var signIn = login.SignInAsync(uri => browser.TrySetResult(uri), timeout.Token);
        var uri = await browser.Task.WaitAsync(timeout.Token);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert(uri.Host == "huggingface.co" && uri.AbsolutePath == "/oauth/authorize" && query["scope"] == "gated-repos", "ANIS authorization must request only the gated-download scope from Hugging Face.");
        var callback = new Uri(query["redirect_uri"].ToString());
        Assert(callback.IsLoopback && callback.AbsolutePath == "/auth/callback", "OAuth must use the app-owned loopback callback.");
        using var local = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        using var wrong = await local.GetAsync(callback + "?state=wrong&code=fixture-code", timeout.Token);
        Assert(wrong.StatusCode == HttpStatusCode.BadRequest && !signIn.IsCompleted, "A callback for another state must not complete authorization.");
        using var right = await local.GetAsync(QueryHelpers.AddQueryString(callback.AbsoluteUri, new Dictionary<string, string?>
        {
            ["state"] = query["state"].ToString(), ["code"] = "fixture-code"
        }), timeout.Token);
        Assert(right.IsSuccessStatusCode && await signIn == OauthCredential, "An authorized callback must accept the dotted OAuth bearer credential and return it internally.");
        Assert(login.IsAuthorized && await login.SignInAsync(_ => throw new Exception("Retry opened a new sign-in page."), timeout.Token) == OauthCredential,
            "A download retry must use the same authorization until the installation succeeds or the user explicitly reconnects.");
        login.ClearAuthorization();
        Assert(!login.IsAuthorized, "A finished download must discard the temporary credential.");
        var verifier = request!["code_verifier"].ToString();
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        Assert(query["code_challenge_method"] == "S256" && query["code_challenge"] == challenge &&
            request["client_id"] == "fixture-public-client" && request["redirect_uri"] == callback.AbsoluteUri &&
            request["code"] == "fixture-code" && !request.ContainsKey("client_secret"), "Public client authorization must bind its code exchange to the original PKCE challenge and callback.");
    }
    public static async Task RuntimeSmokeAsync()
    {
        var root = new LauncherPaths(Path.Combine(Root, "artifacts/anis-runtime-smoke"));
        using var installer = new InstallationService(root); using var backend = new AnisBackend(root);
        var progress = new Progress<InstallationProgress>(p => Console.WriteLine($"{p.Stage}: {p.Message} {p.CompletedBytes}/{p.TotalBytes}"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var runtime = await backend.EnsureRuntimeAsync(installer, progress, timeout.Token);
        Console.WriteLine("ANIS private runtime prepared: " + runtime.PythonExecutable);
        Console.WriteLine("Official source directory: " + runtime.SourceDirectory);
        var model = CatalogLoader.Load(Path.Combine(Root, "src/GemmaLauncher.App/Assets/catalog.json")).Models.Single(model => model.Engine == "anis");
        var configuration = new LaunchConfiguration(new(model, Path.Combine(root.Models, "ct2", "model.bin"), null), runtime.PythonExecutable, 1024, root.Logs)
        { BackendSourceDirectory = runtime.SourceDirectory };
        var info = new System.Diagnostics.ProcessStartInfo(runtime.PythonExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in ServerSession.BuildArguments(configuration).Append("--help")) info.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(true); }
        var help = await stdout; var error = await stderr;
        Assert(process.ExitCode == 0 && help.Contains("--device") && help.Contains("--model"), "Official API entry point failed: " + error);
        Console.WriteLine("Official ANIS API entry point passed with isolated UTF8 launch arguments.");
    }
    private static async Task AssertCancelledAsync(Func<Task> action)
    {
        try { await action(); throw new Exception("Cancelled operation continued."); } catch (OperationCanceledException) { }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class ResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
