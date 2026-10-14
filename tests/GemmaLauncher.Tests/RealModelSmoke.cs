using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

// Explicit opt-in integration run. This never runs as part of the ordinary test suite.
public static class RealModelSmoke
{
    public static async Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var catalog = CatalogLoader.Load(Path.Combine(root, "src/GemmaLauncher.App/Assets/catalog.json"));
        var paths = new LauncherPaths(Path.Combine(root, ".superpowers/launcher-smoke"));
        Directory.CreateDirectory(paths.Root);
        var resultsPath = Path.Combine(paths.Root, "smoke-results.json");
        var progress = new ConsoleProgress();
        using var installer = new InstallationService(paths, searchDirectories:
            [Path.Combine(root, ".superpowers/benchmarks/2026-09-05-models/models")]);
        var report = new SmokeReport { StartedAt = DateTimeOffset.Now, Runtime = catalog.Runtime };
        await WriteReportAsync();
        try
        {
            report.RuntimeExecutable = await installer.EnsureRuntimeAsync(catalog.Runtime, progress);
            await WriteReportAsync();
            foreach (var definition in catalog.Models)
            {
                var result = new ModelResult
                {
                    ModelId = definition.Id, ApiModelId = definition.ApiModelId,
                    Context = definition.Profile.DefaultContext, Lazy = definition.Profile.Lazy,
                    MtpDraftMax = definition.Profile.MtpDraftMax, StartedAt = DateTimeOffset.Now,
                    Port = AvailablePort()
                };
                report.Models.Add(result);
                await WriteReportAsync();
                Console.WriteLine($"Starting smoke: {definition.DisplayName}");
                await using var session = new ServerSession();
                var states = new List<SessionStatus>();
                session.StatusChanged += (_, status) => { lock (states) states.Add(status); };
                try
                {
                    var installed = await installer.EnsureModelAsync(definition, progress);
                    result.ModelPath = installed.ModelPath;
                    result.MtpPath = installed.MtpPath;
                    var startup = Stopwatch.StartNew();
                    await session.StartAsync(new(installed, report.RuntimeExecutable, definition.Profile.DefaultContext, paths.Logs, result.Port));
                    result.StartupSeconds = startup.Elapsed.TotalSeconds;
                    result.ProcessId = session.Status.ProcessId;
                    using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
                    { BaseAddress = new Uri($"http://127.0.0.1:{result.Port}/"), Timeout = TimeSpan.FromMinutes(2) };
                    using var modelsResponse = await client.GetAsync("v1/models");
                    modelsResponse.EnsureSuccessStatusCode();
                    using var models = JsonDocument.Parse(await modelsResponse.Content.ReadAsStringAsync());
                    result.ModelsResponse = models.RootElement.Clone();
                    if (!models.RootElement.GetProperty("data").EnumerateArray().Any(model => model.GetProperty("id").GetString() == definition.ApiModelId))
                        throw new InvalidOperationException("The live API does not advertise the configured model id.");
                    var isTranslation = definition.Id.Contains("translate", StringComparison.OrdinalIgnoreCase);
                    var messages = isTranslation
                        ? new[]
                        {
                            new { role = "system", content = "TASK: Translate English into Korean. Preserve the conversational tone. Return only the translation." },
                            new { role = "user", content = "The captain has already arrived. Open the gate, and tell everyone that we are going home." }
                        }
                        : new[]
                        {
                            new { role = "system", content = "You are a creative writing assistant. Follow the requested length." },
                            new { role = "user", content = "Write two natural sentences of dialogue between a tired spaceship mechanic and her old friend." }
                        };
                    result.Request = JsonSerializer.SerializeToElement(new { model = definition.ApiModelId, messages, max_tokens = 96, seed = 1729, stream = false });
                    var generation = Stopwatch.StartNew();
                    using var completion = await client.PostAsJsonAsync("v1/chat/completions", result.Request);
                    var completionBody = await completion.Content.ReadAsStringAsync();
                    if (!completion.IsSuccessStatusCode) throw new IOException($"Completion failed ({(int)completion.StatusCode}): {completionBody}");
                    using var parsed = JsonDocument.Parse(completionBody);
                    result.Response = parsed.RootElement.Clone();
                    result.Output = parsed.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                    if (string.IsNullOrWhiteSpace(result.Output)) throw new InvalidOperationException("The model returned no text.");
                    result.CompletionSeconds = generation.Elapsed.TotalSeconds;
                    result.Status = "passed";
                    Console.WriteLine($"Completed: {definition.Id}, startup {result.StartupSeconds:0.00}s, response {result.CompletionSeconds:0.00}s");
                }
                catch (Exception exception)
                {
                    result.Status = "failed";
                    result.Error = exception.ToString();
                    Console.WriteLine($"FAILED {definition.Id}: {exception.Message}");
                }
                finally
                {
                    try { await session.StopAsync(); }
                    catch (Exception exception) { result.Status = "failed"; result.Error += "\nShutdown: " + exception; }
                    lock (states)
                    {
                        result.States = [.. states];
                        result.ProcessId ??= states.LastOrDefault(state => state.ProcessId is not null)?.ProcessId;
                    }
                    result.OwnedProcessExited = result.ProcessId is not { } pid || HasExited(pid);
                    if (!result.OwnedProcessExited) { result.Status = "failed"; result.Error += "\nOwned process is still running."; }
                    result.FinishedAt = DateTimeOffset.Now;
                    await WriteReportAsync();
                }
            }
        }
        catch (Exception exception)
        {
            report.Error = exception.ToString();
            throw;
        }
        finally
        {
            report.FinishedAt = DateTimeOffset.Now;
            await WriteReportAsync();
        }
        if (report.Models.Any(model => model.Status != "passed")) throw new InvalidOperationException("A real-model smoke failed. See " + resultsPath);
        Console.WriteLine("All real-model smokes passed. Results: " + resultsPath);

        async Task WriteReportAsync()
        {
            var temporary = resultsPath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(report, CatalogLoader.JsonOptions));
            File.Move(temporary, resultsPath, true);
        }
    }

    private static int AvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool HasExited(int processId)
    {
        try { using var process = Process.GetProcessById(processId); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }

    private sealed class ConsoleProgress : IProgress<InstallationProgress>
    {
        private long _lastLog;
        private string? _lastStage;
        public void Report(InstallationProgress value)
        {
            var now = Environment.TickCount64;
            if (_lastStage == value.Stage && now - _lastLog < 3000) return;
            _lastStage = value.Stage; _lastLog = now;
            Console.WriteLine($"{value.Stage}: {value.Message} {value.Percent:0}%");
        }
    }

    private sealed class SmokeReport
    {
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? FinishedAt { get; set; }
        public required RuntimePackage Runtime { get; init; }
        public string? RuntimeExecutable { get; set; }
        public List<ModelResult> Models { get; } = [];
        public string? Error { get; set; }
    }

    private sealed class ModelResult
    {
        public required string ModelId { get; init; }
        public required string ApiModelId { get; init; }
        public int Context { get; init; }
        public required string Lazy { get; init; }
        public int MtpDraftMax { get; init; }
        public int Port { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string Status { get; set; } = "running";
        public string? ModelPath { get; set; }
        public string? MtpPath { get; set; }
        public int? ProcessId { get; set; }
        public double? StartupSeconds { get; set; }
        public double? CompletionSeconds { get; set; }
        public JsonElement? ModelsResponse { get; set; }
        public JsonElement? Request { get; set; }
        public JsonElement? Response { get; set; }
        public string? Output { get; set; }
        public string? Error { get; set; }
        public bool OwnedProcessExited { get; set; }
        public SessionStatus[] States { get; set; } = [];
    }
}
