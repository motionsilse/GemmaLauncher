using System.Net;
using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

public static class AnisTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    public static async Task RunAsync()
    {
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
