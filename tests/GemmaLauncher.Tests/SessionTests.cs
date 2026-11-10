using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

public static class SessionTests
{
    // The test runner also acts as a tiny server child. Production never enters this path.
    public static async Task<bool> TryRunChildAsync(string[] arguments)
    {
        var modelPath = Argument(arguments, "--model");
        var anis = arguments.Any(argument => argument.Contains("run_module('anis_trans.server'", StringComparison.Ordinal));
        if (anis && modelPath is not null) modelPath = Path.Combine(modelPath, "session-fixture-anis.json");
        if (modelPath is null || !Path.GetFileName(modelPath).StartsWith("session-fixture-", StringComparison.Ordinal)) return false;
        var mode = await File.ReadAllTextAsync(modelPath);
        if (mode == "oom") { Console.Error.WriteLine("fixture: out of memory"); Environment.ExitCode = 37; return true; }
        if (mode == "oom-vulkan") { Console.Error.WriteLine("vk::Device::allocateMemory: ErrorOutOfDeviceMemory"); Environment.ExitCode = 37; return true; }
        if (mode == "fail") { Environment.ExitCode = 37; return true; }
        if (mode == "slow") { await Task.Delay(TimeSpan.FromMinutes(1)); return true; }
        if (mode == "with-child")
        {
            var childPath = Path.ChangeExtension(modelPath, "child.gguf");
            await File.WriteAllTextAsync(childPath, "slow");
            var childStart = new ProcessStartInfo(FixtureExecutablePath()) { UseShellExecute = false, CreateNoWindow = true };
            childStart.ArgumentList.Add("--model");
            childStart.ArgumentList.Add(childPath);
            using var child = Process.Start(childStart)!;
            await File.WriteAllTextAsync(modelPath + ".childpid", child.Id.ToString(CultureInfo.InvariantCulture));
        }
        var port = int.Parse(Argument(arguments, "--port")!, CultureInfo.InvariantCulture);
        var alias = anis ? "anis-trans:auto" : Argument(arguments, "--alias")!;
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        if (mode == "verbose")
        {
            _ = Task.Run(async () =>
            {
                var block = new string('x', 8192);
                for (var count = 0; count < 800; count++)
                {
                    await Console.Out.WriteAsync(block);
                    await Console.Error.WriteAsync(block);
                }
            });
        }
        while (true)
        {
            using var connection = await listener.AcceptTcpClientAsync();
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var first = await reader.ReadLineAsync();
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) { }
            var body = first?.Contains("/v1/models", StringComparison.Ordinal) == true
                ? JsonSerializer.Serialize(new { data = new[] { new { id = mode == "wrong-alias" ? "someone-else" : alias } } })
                : "{\"status\":\"ok\"}";
            var status = 200;
            if (anis)
            {
                var path = Uri.UnescapeDataString(first?.Split(' ').ElementAtOrDefault(1) ?? "");
                if (path == "/health") status = 404;
                if (path == "/v1/models") body = "{\"data\":[{\"id\":\"anis-trans:latest\"}]}";
                else if (path.StartsWith("/v1/models/", StringComparison.Ordinal)) body = JsonSerializer.Serialize(new { id = path["/v1/models/".Length..] });
            }
            var payload = Encoding.UTF8.GetBytes(body);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(payload);
        }
    }

    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GemmaLauncher-session-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = CreateConfiguration(directory, "ready");
            await CheckAnisReadinessAsync(configuration, directory);
            CheckArguments(configuration);
            await CheckInvalidContextAsync(configuration);
            await CheckPortConflictAsync(configuration);
            await CheckRepeatedLifecycleAsync(configuration);
            await CheckRepeatedLifecycleAsync(configuration with { EnableGateway = true, Preset = TaskPreset.Empty });
            await CheckCancellationAsync(CreateConfiguration(directory, "slow") with { EnableGateway = true });
            await CheckCancellationAsync(CreateConfiguration(directory, "slow"));
            await CheckStopDuringStartupAsync(CreateConfiguration(directory, "slow"));
            await CheckStartupFailureAsync(CreateConfiguration(directory, "fail"));
            await CheckMemoryFailureAsync(CreateConfiguration(directory, "oom"));
            await CheckMemoryFailureAsync(CreateConfiguration(directory, "oom-vulkan"));
            await CheckAliasMismatchAsync(CreateConfiguration(directory, "wrong-alias"));
            await CheckCrashAsync(CreateConfiguration(directory, "ready"));
            await CheckDisposeAsync(CreateConfiguration(directory, "ready"));
            await CheckDescendantsAsync(CreateConfiguration(directory, "with-child"));
            await CheckVerboseLogAsync(CreateConfiguration(directory, "verbose"));
        }
        finally
        {
            // Only this invocation's uniquely named directory is removed.
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckArguments(LaunchConfiguration configuration)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var args = ServerSession.BuildArguments(configuration).ToArray();
            Assert(Argument(args, "--alias") == configuration.Model.Definition.ApiModelId, "API alias must match the copied model id.");
            Assert(Argument(args, "--host") == "127.0.0.1", "Server must stay on loopback.");
            Assert(Argument(args, "--port") == configuration.Port.ToString(CultureInfo.InvariantCulture), "Port mismatch.");
            Assert(Argument(args, "--lazy-mode") == "on", "Translation memory optimization missing.");
            Assert(Argument(args, "--spec-type") == "draft-mtp" && Argument(args, "--spec-draft-n-max") == "2", "Translation MTP profile mismatch.");
            Assert(Argument(args, "--load-mode") == "mmap" && Argument(args, "--fit") == "off", "Verified loading settings missing.");
            Assert(Argument(args, "--cache-type-k") == "q8_0" && Argument(args, "--cache-type-v") == "q8_0", "KV cache settings missing.");
            Assert(Argument(args, "--temperature") == "0.7" && Argument(args, "--top-p") == "0.95", "Numeric arguments must use invariant culture.");
            Assert(Argument(args, "--model") == Path.GetFullPath(configuration.Model.ModelPath), "Model path must stay one argument, including spaces.");
            var large = configuration with
            {
                Context = 32768,
                Model = configuration.Model with
                {
                    Definition = configuration.Model.Definition with
                    {
                        Profile = configuration.Model.Definition.Profile with { ContextSizes = [32768], Lazy = "auto", MtpDraftMax = 4 }
                    }
                }
            };
            var largeArgs = ServerSession.BuildArguments(large).ToArray();
            Assert(Argument(largeArgs, "--ctx-size") == "32768" && Argument(largeArgs, "--spec-draft-n-max") == "4" && Argument(largeArgs, "--lazy-mode") == "auto", "12B profile must not inherit E2B settings.");
            var noMtp = ServerSession.BuildArguments(configuration with { Model = configuration.Model with { MtpPath = null } }).ToArray();
            Assert(Argument(noMtp, "--spec-type") == "none" && Argument(noMtp, "--spec-draft-model") is null, "Additional non-MTP models must remain supported.");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }
    private static async Task CheckAnisReadinessAsync(LaunchConfiguration original, string directory)
    {
        var modelRoot = Path.Combine(directory, "anis-model"); Directory.CreateDirectory(Path.Combine(modelRoot, "ct2"));
        var main = Path.Combine(modelRoot, "ct2", "model.bin"); await File.WriteAllTextAsync(main, "fixture");
        await File.WriteAllTextAsync(Path.Combine(modelRoot, "session-fixture-anis.json"), "anis-ready");
        var source = Path.Combine(directory, "source", "anis_trans"); Directory.CreateDirectory(source); await File.WriteAllTextAsync(Path.Combine(source, "server.py"), "# fixture");
        var model = original.Model.Definition with { Id = "anis-trans", Engine = "anis", ApiModelId = "anis-trans:auto", Profile = original.Model.Definition.Profile with { DefaultContext = 1024, ContextSizes = [1024], MtpDraftMax = 0 } };
        var configuration = original with { Model = new(model, main, null), BackendSourceDirectory = Path.GetDirectoryName(source), Context = 1024, EnableGateway = true };
        await using var session = new ServerSession(TimeSpan.FromSeconds(3));
        await session.StartAsync(configuration);
        Assert(session.Status.State == ServerState.Running, "ANIS must become ready without /health or the auto alias being advertised in /v1/models.");
        await session.StopAsync();
    }
    private static async Task CheckMemoryFailureAsync(LaunchConfiguration configuration)
    {
        await using var session = new ServerSession(TimeSpan.FromSeconds(3));
        try { await session.StartAsync(configuration); throw new Exception("A memory failure was not detected."); }
        catch (InvalidOperationException ex) { Assert(ex.InnerException is ModelLoadMemoryException && session.Status.State == ServerState.Faulted, "An engine memory error must retain a typed cause for the recovery UI."); }
    }

    private static async Task CheckInvalidContextAsync(LaunchConfiguration configuration)
    {
        await using var session = new ServerSession();
        await ExpectAsync<ArgumentException>(() => session.StartAsync(configuration with { Context = 9999 }));
        Assert(session.Status.State == ServerState.Stopped && session.Status.ProcessId is null, "Invalid configuration must not spawn a child.");
    }

    private static async Task CheckPortConflictAsync(LaunchConfiguration configuration)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var session = new ServerSession();
        await ExpectAsync<InvalidOperationException>(() => session.StartAsync(configuration with { Port = port }));
        Assert(session.Status.State == ServerState.Faulted && session.Status.ProcessId is null, "An occupied port must fail before launching.");
        Assert(session.Status.Message.Contains(port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), "Port conflict must identify the port.");
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var accepted = await listener.AcceptTcpClientAsync();
        Assert(accepted.Connected, "Existing listener must survive a launch attempt.");
        await session.StopAsync();
        Assert(session.Status.State == ServerState.Stopped, "Faulted session should be resettable.");
    }

    private static async Task CheckRepeatedLifecycleAsync(LaunchConfiguration configuration)
    {
        await using var session = new ServerSession(TimeSpan.FromSeconds(10));
        await session.StartAsync(configuration);
        var firstPid = session.Status.ProcessId!.Value;
        Assert(session.Status.State == ServerState.Running, "Server should become ready.");
        await session.StartAsync(configuration);
        Assert(session.Status.ProcessId == firstPid, "Repeated Start must not spawn another process.");
        await ExpectAsync<InvalidOperationException>(() => session.StartAsync(configuration with { Context = 4096 }));
        Assert(session.Status.ProcessId == firstPid && session.Status.State == ServerState.Running, "Rejected reconfiguration must preserve the running server.");
        await session.StopAsync();
        AssertExited(firstPid);
        await session.StopAsync();
        Assert(session.Status.State == ServerState.Stopped, "Stop must be idempotent.");
        await session.StartAsync(configuration);
        Assert(session.Status.State == ServerState.Running, "A stopped session must restart.");
        var secondPid = session.Status.ProcessId!.Value;
        await session.StopAsync();
        AssertExited(secondPid);
    }

    private static async Task CheckCancellationAsync(LaunchConfiguration configuration)
    {
        await using var session = new ServerSession(TimeSpan.FromSeconds(10));
        int? spawned = null;
        session.StatusChanged += (_, status) => spawned ??= status.ProcessId;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await ExpectAsync<OperationCanceledException>(() => session.StartAsync(configuration, cancellation.Token));
        Assert(spawned is not null, "Cancellation test must exercise an actual child.");
        AssertExited(spawned ?? throw new InvalidOperationException("Fixture process was not created."));
        Assert(session.Status.State == ServerState.Stopped, "Cancelled startup must finish Stopped.");
    }

    private static async Task CheckStopDuringStartupAsync(LaunchConfiguration configuration)
    {
        await using var session = new ServerSession(TimeSpan.FromSeconds(10));
        var spawned = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StatusChanged += (_, status) => { if (status.ProcessId is { } pid) spawned.TrySetResult(pid); };
        var starting = session.StartAsync(configuration);
        var processId = await spawned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync();
        await ExpectAsync<OperationCanceledException>(() => starting);
        AssertExited(processId);
        Assert(session.Status.State == ServerState.Stopped, "Stop during startup must not become Running later.");
    }

    private static async Task CheckStartupFailureAsync(LaunchConfiguration configuration)
    {
        await using var session = new ServerSession(TimeSpan.FromSeconds(10));
        int? spawned = null;
        session.StatusChanged += (_, status) => spawned ??= status.ProcessId;
        await ExpectAsync<InvalidOperationException>(() => session.StartAsync(configuration));
        Assert(session.Status.State == ServerState.Faulted && session.Status.Message.Contains("실행 기록:", StringComparison.Ordinal), "Early exit needs a readable error and log path.");
        if (spawned is { } processId) AssertExited(processId);
    }

    private static async Task CheckAliasMismatchAsync(LaunchConfiguration configuration)
    {
        await using var session = new ServerSession(TimeSpan.FromSeconds(1));
        int? spawned = null;
        session.StatusChanged += (_, status) => spawned ??= status.ProcessId;
        await ExpectAsync<InvalidOperationException>(() => session.StartAsync(configuration));
        Assert(session.Status.State == ServerState.Faulted && session.Status.Message.Contains("초과", StringComparison.Ordinal), "A healthy wrong model must not be marked Running.");
        if (spawned is { } processId) AssertExited(processId);
    }

    private static async Task CheckCrashAsync(LaunchConfiguration configuration)
    {
        await using var session = new ServerSession(TimeSpan.FromSeconds(10));
        var faulted = new TaskCompletionSource<SessionStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StatusChanged += (_, status) => { if (status.State == ServerState.Faulted) faulted.TrySetResult(status); };
        await session.StartAsync(configuration);
        using var child = Process.GetProcessById(session.Status.ProcessId!.Value);
        child.Kill();
        var status = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(status.ProcessId is null && status.Message.Contains("예기치", StringComparison.Ordinal), "Crash must remove stale running state.");
        await session.StartAsync(configuration);
        Assert(session.Status.State == ServerState.Running, "A crashed session must restart.");
        await session.StopAsync();
    }

    private static async Task CheckDisposeAsync(LaunchConfiguration configuration)
    {
        var session = new ServerSession(TimeSpan.FromSeconds(10));
        await session.StartAsync(configuration);
        var processId = session.Status.ProcessId!.Value;
        await session.DisposeAsync();
        await session.DisposeAsync();
        AssertExited(processId);
        await ExpectAsync<ObjectDisposedException>(() => session.StartAsync(configuration));
    }

    private static async Task CheckVerboseLogAsync(LaunchConfiguration configuration)
    {
        await using var session = new ServerSession(TimeSpan.FromSeconds(10));
        await session.StartAsync(configuration);
        // Both pipes exceed their OS buffers; readiness already demonstrates they are drained.
        await Task.Delay(500);
        await session.StopAsync();
        var files = Directory.GetFiles(configuration.LogDirectory, "server-*.log");
        Assert(files.Length <= 10, "Old logs should be bounded.");
        Assert(files.All(path => new FileInfo(path).Length <= 5 * 1024 * 1024), "One verbose launch must not fill the disk.");
    }

    private static async Task CheckDescendantsAsync(LaunchConfiguration configuration)
    {
        await using var session = new ServerSession(TimeSpan.FromSeconds(10));
        await session.StartAsync(configuration);
        var childId = int.Parse(await File.ReadAllTextAsync(configuration.Model.ModelPath + ".childpid"), CultureInfo.InvariantCulture);
        var parentId = session.Status.ProcessId!.Value;
        await session.StopAsync();
        AssertExited(parentId);
        AssertExited(childId);
    }

    private static LaunchConfiguration CreateConfiguration(string directory, string mode)
    {
        var path = Path.Combine(directory, $"session-fixture-{Guid.NewGuid():N} with spaces.gguf");
        File.WriteAllText(path, mode);
        var definition = new ModelDefinition
        {
            Id = "fixture", DisplayName = "Fixture", ApiModelId = "fixture-model", Category = "test", Headline = "test",
            Description = "test", HardwareGuide = "test", MemoryGuide = "test", SourceUrl = "https://example.invalid",
            LicenseUrl = "https://example.invalid", Profile = new ModelProfile { Temperature = .7 }, Artifacts = []
        };
        using var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        return new(new InstalledModel(definition, path, path), FixtureExecutablePath(), 8192, Path.Combine(directory, "logs"), port);
    }

    private static string FixtureExecutablePath()
    {
        // Environment.ProcessPath can be dotnet.exe when the runner is launched through its DLL.
        var path = Path.Combine(AppContext.BaseDirectory, "GemmaLauncher.Tests.exe");
        if (!File.Exists(path))
            throw new FileNotFoundException("The test apphost is missing. Build GemmaLauncher.Tests with UseAppHost=true before running session tests.", path);
        return path;
    }

    private static string? Argument(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            Assert(process.HasExited, $"Owned fixture process {processId} leaked.");
        }
        catch (ArgumentException) { }
    }

    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
