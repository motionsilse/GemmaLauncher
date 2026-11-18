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
    public static async Task RunAsync(string? testDirectory = null)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var catalog = CatalogLoader.Load(Path.Combine(root, "src/GemmaLauncher.App/Assets/catalog.json"));
        var paths = new LauncherPaths(testDirectory ?? Path.Combine(root, ".superpowers/launcher-smoke"));
        Directory.CreateDirectory(paths.Root);
        var resultsPath = Path.Combine(paths.Root, "smoke-results.json");
        var progress = new ConsoleProgress();
        var cacheDirectories = new[]
        {
            new LauncherPaths().Models,
            Path.Combine(root, ".superpowers/benchmarks/2026-09-05-models/models"),
            Path.Combine(root, "artifacts/cat-loader/translation-benchmark-20261111/cache/models")
        };
        var cachedFolders = cacheDirectories.Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.gguf", SearchOption.AllDirectories))
            .Select(Path.GetDirectoryName).OfType<string>();
        using var installer = new InstallationService(paths, searchDirectories: cachedFolders);
        var report = new SmokeReport { StartedAt = DateTimeOffset.Now, Runtime = catalog.Runtime };
        await WriteReportAsync();
        try
        {
            report.RuntimeExecutable = await installer.EnsureRuntimeAsync(catalog.Runtime, progress);
            await WriteReportAsync();
            foreach (var definition in catalog.Models.Where(model => model.Engine == "llama").OrderBy(model => model.Architecture != "plamo3"))
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
                    var plamo = definition.Architecture == "plamo3";
                    await session.StartAsync(new(installed, report.RuntimeExecutable, definition.Profile.DefaultContext, paths.Logs, result.Port)
                    {
                        EnableGateway = true,
                        Preset = plamo ? TaskPreset.Empty with { Prompt = "This preset must not become part of the translation." } : TaskPreset.Empty
                    });
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
                    if (plamo)
                    {
                        await CheckPlamoAsync(client, definition, result);
                        if (result.ProcessId is int pid)
                        {
                            using var metrics = new ProcessMetrics(pid);
                            result.Memory = metrics.Read();
                        }
                        result.Status = "passed";
                        Console.WriteLine($"Completed: {definition.Id}, startup {result.StartupSeconds:0.00}s, both Japanese/Korean directions");
                        continue;
                    }
                    var isTranslation = definition.FamilyId is "gemma-translation" or "hy-mt2";
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

    private static async Task CheckPlamoAsync(HttpClient client, ModelDefinition definition, ModelResult result)
    {
        await TranslateAsync(["列車は明日の朝七時に出発します。"], new { target_lang = "ko" }, "ja-ko",
            text => text.Any(character => character is >= '\uAC00' and <= '\uD7A3'));
        await TranslateAsync(["[plamo-translate] target_lang=ja", "기차는 내일 아침 일곱 시에 출발합니다."], null, "ko-ja",
            text => text.Any(character => character is >= '\u3041' and <= '\u30FF'));

        async Task TranslateAsync(string[] contents, object? templateOptions, string direction, Func<string, bool> targetScript)
        {
            var messages = contents.Select((text, i) => new { role = i == contents.Length - 1 ? "user" : "system", content = text });
            var request = new Dictionary<string, object>
            {
                ["model"] = definition.ApiModelId,
                ["messages"] = messages,
                ["max_tokens"] = 128
            };
            if (templateOptions is not null) request["chat_template_kwargs"] = templateOptions;
            var timer = Stopwatch.StartNew();
            using var response = await client.PostAsJsonAsync("v1/chat/completions", request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new IOException($"Translation failed ({(int)response.StatusCode}): {body}");
            using var document = JsonDocument.Parse(body);
            var choice = document.RootElement.GetProperty("choices")[0];
            var output = choice.GetProperty("message").GetProperty("content").GetString();
            if (choice.GetProperty("finish_reason").GetString() != "stop" || string.IsNullOrWhiteSpace(output) ||
                !targetScript(output) || output.Contains("<|plamo:") || output.Contains("preset", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Expected clean {direction} translation: {output}");
            result.Translations.Add(new(direction, output, timer.Elapsed.TotalMilliseconds));
        }
    }

    private sealed record TranslationResult(string Direction, string Output, double Milliseconds);

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
        public List<TranslationResult> Translations { get; } = [];
        public ProcessMemorySnapshot? Memory { get; set; }
        public JsonElement? ModelsResponse { get; set; }
        public JsonElement? Request { get; set; }
        public JsonElement? Response { get; set; }
        public string? Output { get; set; }
        public string? Error { get; set; }
        public bool OwnedProcessExited { get; set; }
        public SessionStatus[] States { get; set; } = [];
    }
}
