using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml.Linq;
using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

/// <summary>Opt-in real-account, fresh-download test. No browser or desktop automation.</summary>
internal static class AnisLiveSmoke
{
    public static async Task RunAsync(string directory)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var paths = new LauncherPaths(Path.GetFullPath(directory));
        if (Directory.Exists(paths.Root)) throw new InvalidOperationException("Live test requires a new, nonexistent directory.");
        Directory.CreateDirectory(paths.Root);
        var catalog = CatalogLoader.Load(Path.Combine(root, "src/GemmaLauncher.App/Assets/catalog.json"));
        var model = catalog.Models.Single(model => model.Id == "anis-trans");
        var clientId = XDocument.Load(Path.Combine(root, "src/GemmaLauncher.App/GemmaLauncher.App.csproj"))
            .Descendants("HuggingFaceClientId").Single().Value;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        Console.CancelKeyPress += Cancel;
        try
        {
            using var http = new HttpClient(new AuditHandler()) { Timeout = Timeout.InfiniteTimeSpan };
            using var backend = new AnisBackend(paths, client: http);
            using var authorization = new HuggingFaceAuthorization(clientId);
            var progress = new TransferProgress();
            Assert(await backend.FindAsync(model, lifetime.Token) is null, "The model must not already exist.");
            Console.WriteLine("LIVE TEST DATA: " + paths.Root);
            InstalledModel installed;
            var signedIn = false;
            var download = Stopwatch.StartNew();
            try { installed = await backend.EnsureModelAsync(model, null, progress, lifetime.Token); }
            catch (AnisAccessRequiredException)
            {
                Console.WriteLine("LOGIN REQUIRED: Complete Hugging Face sign-in and Cat Loader authorization in the browser. No automation is used.");
                Console.Out.Flush();
                var credential = await authorization.SignInAsync(uri =>
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }), lifetime.Token);
                signedIn = true;
                Console.WriteLine("LOGIN COMPLETE: Real OAuth code exchange succeeded. Credential stays in memory.");
                download.Restart();
                installed = await backend.EnsureModelAsync(model, credential, progress, lifetime.Token);
                authorization.ClearAuthorization();
            }
            download.Stop();
            var expectedBytes = AnisBackend.RuntimeModelFiles.Sum(file => file.Bytes);
            Assert(progress.CompletedBytes == expectedBytes, "All model bytes must come through the real downloader.");
            Assert(!Directory.EnumerateFiles(paths.Models, "*.partial", SearchOption.AllDirectories).Any(), "No partial files may remain after download.");
            using (var verifier = new AnisBackend(paths))
                Assert(await verifier.FindAsync(model, lifetime.Token) is not null, "A new backend must verify all downloaded files from disk.");
            Console.WriteLine($"DOWNLOAD VERIFIED: {AnisBackend.RuntimeModelFiles.Count} files, {expectedBytes} bytes, {download.Elapsed.TotalSeconds:F2}s");

            using var installer = new InstallationService(paths, discoverExistingModels: false);
            var preparation = Stopwatch.StartNew();
            var runtime = await backend.EnsureRuntimeAsync(installer, new TransferProgress(), lifetime.Token);
            preparation.Stop();
            Console.WriteLine($"RUNTIME READY: {preparation.Elapsed.TotalSeconds:F2}s");
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            await using var session = new ServerSession();
            var configuration = new LaunchConfiguration(installed, runtime.PythonExecutable, model.Profile.DefaultContext, paths.Logs, port)
            { BackendSourceDirectory = runtime.SourceDirectory, AnisDevice = "auto", EnableGateway = true, Preset = TaskPreset.Empty };
            var load = Stopwatch.StartNew();
            await session.StartAsync(configuration, lifetime.Token);
            load.Stop();
            Assert(session.Status.State == ServerState.Running, "The actual session must reach running state.");
            Console.WriteLine($"MODEL LOADED: {load.Elapsed.TotalSeconds:F2}s; API model: {model.ApiModelId}");

            using var api = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{port}/v1/"), Timeout = TimeSpan.FromSeconds(30) };
            var inputs = new[]
            {
                ("warmup", "こんにちは。"),
                ("ja-ko", "明日は早く起きて、朝ごはんを作ります。"),
                ("ja-ko", "駅まで歩いて何分くらいかかりますか？"),
                ("ja-ko", "今日は雨が降っているので、傘を持っていってください。"),
                ("ja-ko", "注文した商品はいつ届きますか？"),
                ("ja-ko", "手伝ってくれてありがとうございます。"),
                ("ko-ja", "내일은 일찍 일어나서 아침밥을 만들 거예요."),
                ("ko-ja", "역까지 걸어서 몇 분 정도 걸리나요?"),
                ("ko-ja", "오늘은 비가 오니까 우산을 가지고 가세요."),
                ("ko-ja", "주문한 상품은 언제 도착하나요?"),
                ("ko-ja", "도와주셔서 감사합니다.")
            };
            var translations = new List<object>();
            foreach (var (direction, input) in inputs)
            {
                var timer = Stopwatch.StartNew();
                using var response = await api.PostAsJsonAsync("chat/completions", new
                {
                    model = model.ApiModelId,
                    messages = new[] { new { role = "user", content = input } },
                    stream = false
                }, lifetime.Token);
                var responseBody = await response.Content.ReadAsStringAsync(lifetime.Token);
                Assert(response.IsSuccessStatusCode, $"Translation HTTP {(int)response.StatusCode}: {responseBody}");
                using var json = JsonDocument.Parse(responseBody);
                var output = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                timer.Stop();
                Assert(direction == "ko-ja" ? output.Any(c => c is >= '\u3040' and <= '\u30ff') : output.Any(c => c is >= '가' and <= '힣'), "Translation must contain the target language: " + output);
                translations.Add(new { direction, input, output, seconds = timer.Elapsed.TotalSeconds });
                Console.WriteLine($"{direction}: {timer.Elapsed.TotalSeconds:F3}s | {output}");
            }
            await session.StopAsync(lifetime.Token);
            Assert(session.Status.State == ServerState.Stopped, "Stopping must terminate the owned model session.");
            await session.StartAsync(configuration, lifetime.Token);
            Assert(session.Status.State == ServerState.Running, "Downloaded files must load again without login or download.");
            await session.StopAsync(lifetime.Token);
            var report = new { success = true, signedIn, modelBytes = expectedBytes, modelFileCount = AnisBackend.RuntimeModelFiles.Count,
                downloadSeconds = download.Elapsed.TotalSeconds, runtimeSeconds = preparation.Elapsed.TotalSeconds,
                loadSeconds = load.Elapsed.TotalSeconds, restartedWithoutLogin = true, translations };
            await File.WriteAllTextAsync(Path.Combine(paths.Root, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("LIVE E2E PASSED: login, network download, hash verification, fresh runtime, model load, translation in both directions, stop and reload.");
        }
        finally { Console.CancelKeyPress -= Cancel; }

        void Cancel(object? sender, ConsoleCancelEventArgs e) { e.Cancel = true; lifetime.Cancel(); }
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class TransferProgress : IProgress<InstallationProgress>
    {
        private string? stage;
        private long lastReport;
        public long CompletedBytes { get; private set; }
        public void Report(InstallationProgress value)
        {
            if (value.Stage == "downloading") CompletedBytes = value.CompletedBytes;
            var now = Stopwatch.GetTimestamp();
            if (value.Stage != stage || value.Percent == 100 || Stopwatch.GetElapsedTime(lastReport, now) >= TimeSpan.FromSeconds(2))
            {
                stage = value.Stage; lastReport = now;
                Console.WriteLine($"{value.Stage}: {value.Message} {value.Percent:F1}% {value.CompletedBytes}/{value.TotalBytes}");
                Console.Out.Flush();
            }
        }
    }

    private sealed class AuditHandler : DelegatingHandler
    {
        public AuditHandler() : base(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(30) }) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            Console.WriteLine($"HTTP {request.RequestUri!.Host}: {(int)response.StatusCode}, authenticated={request.Headers.Authorization is not null}, bytes={response.Content.Headers.ContentLength}");
            return response;
        }
    }

}
