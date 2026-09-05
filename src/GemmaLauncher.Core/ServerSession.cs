using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GemmaLauncher.Core;

public sealed class ServerSession : IServerSession
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly object _sync = new();
    private readonly TimeSpan _startupTimeout;
    private SessionStatus _status = new(ServerState.Stopped, Localization.T("engine.session.stopped"));
    private RunningProcess? _owned;
    private CancellationTokenSource? _startup;
    private int _stopRequests;
    private bool _disposed;
    private Task? _disposeTask;

    public ServerSession() : this(TimeSpan.FromMinutes(3)) { }

    public ServerSession(TimeSpan startupTimeout)
    {
        if (startupTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(startupTimeout));
        _startupTimeout = startupTimeout;
    }

    public SessionStatus Status { get { lock (_sync) return _status; } }
    public event EventHandler<SessionStatus>? StatusChanged;

    public static IReadOnlyList<string> BuildArguments(LaunchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var model = configuration.Model.Definition;
        var profile = model.Profile;
        if (!profile.ContextSizes.Contains(configuration.Context) || configuration.Context <= 0)
            throw new ArgumentException(Localization.T("engine.session.invalidContext"), nameof(configuration));
        if (configuration.Port is < 1 or > 65535)
            throw new ArgumentException(Localization.T("engine.session.invalidPort"), nameof(configuration));
        if (profile.Lazy is not ("on" or "off" or "auto"))
            throw new ArgumentException(Localization.T("engine.session.invalidLazy"), nameof(configuration));
        if (string.IsNullOrWhiteSpace(model.ApiModelId))
            throw new ArgumentException(Localization.T("engine.session.missingAlias"), nameof(configuration));
        if (!double.IsFinite(profile.Temperature) || profile.Temperature < 0 ||
            !double.IsFinite(profile.TopP) || profile.TopP is < 0 or > 1 ||
            !double.IsFinite(profile.MinP) || profile.MinP is < 0 or > 1 || profile.TopK < 0)
            throw new ArgumentException(Localization.T("engine.session.invalidSampling"), nameof(configuration));

        List<string> arguments = [
            "--model", Path.GetFullPath(configuration.Model.ModelPath), "--alias", model.ApiModelId,
            "--host", "127.0.0.1", "--port", configuration.Port.ToString(CultureInfo.InvariantCulture),
            "--ctx-size", configuration.Context.ToString(CultureInfo.InvariantCulture),
            "--n-gpu-layers", "all", "--flash-attn", "on", "--cache-type-k", "q8_0", "--cache-type-v", "q8_0",
            "--batch-size", "512", "--ubatch-size", "256", "--parallel", "1", "--load-mode", "mmap",
            "--lazy-mode", profile.Lazy, "--jinja", "--reasoning", "off", "--metrics",
            "--temperature", profile.Temperature.ToString(CultureInfo.InvariantCulture),
            "--top-p", profile.TopP.ToString(CultureInfo.InvariantCulture),
            "--top-k", profile.TopK.ToString(CultureInfo.InvariantCulture),
            "--min-p", profile.MinP.ToString(CultureInfo.InvariantCulture),
            "--repeat-penalty", "1.0", "--fit", "off"
        ];
        if (configuration.Model.MtpPath is { } mtp)
        {
            if (profile.MtpDraftMax is < 1 or > 32)
                throw new ArgumentException(Localization.T("engine.session.invalidDraft"), nameof(configuration));
            arguments.AddRange(["--spec-draft-model", Path.GetFullPath(mtp), "--spec-type", "draft-mtp",
                "--spec-draft-n-max", profile.MtpDraftMax.ToString(CultureInfo.InvariantCulture), "--spec-draft-ngl", "all"]);
        }
        else arguments.AddRange(["--spec-type", "none"]);
        return arguments.AsReadOnly();
    }

    public async Task StartAsync(LaunchConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var arguments = BuildArguments(configuration);
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? startup = null;
        string? logPath = null;
        var began = false;
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_stopRequests > 0) throw new OperationCanceledException(Localization.T("engine.session.startCancelled"));
                if (_owned is not null && !_owned.Process.HasExited)
                {
                    if (_owned.Configuration == configuration && _status.State == ServerState.Running) return;
                    throw new InvalidOperationException(Localization.T("engine.session.alreadyRunning"));
                }
                startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _startup = startup;
            }
            if (_owned is not null) await CleanupOwnedAsync().ConfigureAwait(false);
            startup.Token.ThrowIfCancellationRequested();
            began = true;
            SetStatus(new(ServerState.Starting, Localization.T("engine.session.preparing"), ModelId: configuration.Model.Definition.Id));
            RequireFile(configuration.RuntimeExecutable, Localization.T("engine.session.missingRuntime"));
            RequireFile(configuration.Model.ModelPath, Localization.T("engine.session.missingModel"));
            if (configuration.Model.MtpPath is { } mtp) RequireFile(mtp, Localization.T("engine.session.missingMtp"));
            EnsurePortAvailable(configuration.Port);
            Directory.CreateDirectory(configuration.LogDirectory);
            TrimOldLogs(configuration.LogDirectory);
            logPath = Path.Combine(Path.GetFullPath(configuration.LogDirectory), $"server-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.log");
            var log = new BoundedLog(logPath);
            WindowsJob? job = null;
            Process? process = null;
            try
            {
                job = new WindowsJob();
                var startInfo = new ProcessStartInfo(Path.GetFullPath(configuration.RuntimeExecutable))
                {
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(configuration.RuntimeExecutable))!,
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
                process = new Process { StartInfo = startInfo };
                startup.Token.ThrowIfCancellationRequested();
                if (!process.Start()) throw new IOException(Localization.T("engine.session.launchFailed"));
                // Store ownership before assignment so even a failed Job assignment is cleaned up.
                _owned = new(process, job, log, configuration);
                _owned.Output = PumpAsync(process.StandardOutput, log, "out");
                _owned.Error = PumpAsync(process.StandardError, log, "err");
                job.Assign(process);
            }
            catch
            {
                if (_owned is null)
                {
                    process?.Dispose();
                    job?.Dispose();
                    await log.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
            SetStatus(new(ServerState.Starting, Localization.T("engine.session.loading"), _owned.Process.Id, configuration.Model.Definition.Id));
            await WaitUntilReadyAsync(_owned, startup.Token).ConfigureAwait(false);
            startup.Token.ThrowIfCancellationRequested();
            SetStatus(new(ServerState.Running, Localization.T("engine.session.running"), _owned.Process.Id, configuration.Model.Definition.Id));
            _ = MonitorExitAsync(_owned);
        }
        catch (OperationCanceledException) when (began)
        {
            await CleanupOwnedAsync().ConfigureAwait(false);
            SetStatus(new(ServerState.Stopped, Localization.T("engine.session.cancelled")));
            throw;
        }
        catch (Exception exception) when (began)
        {
            await CleanupOwnedAsync().ConfigureAwait(false);
            var message = logPath is null ? exception.Message : Localization.T("engine.session.failureLog", exception.Message, logPath);
            SetStatus(new(ServerState.Faulted, message));
            throw new InvalidOperationException(message, exception);
        }
        finally
        {
            lock (_sync) { if (ReferenceEquals(_startup, startup)) _startup = null; }
            startup?.Dispose();
            _operation.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _stopRequests++;
            _startup?.Cancel();
        }
        var acquired = false;
        try
        {
            await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            if (_owned is not null) SetStatus(new(ServerState.Stopping, Localization.T("engine.session.stopping"), _owned.Process.Id));
            // Once shutdown starts, finish releasing the owned process even if the caller cancels.
            await CleanupOwnedAsync().ConfigureAwait(false);
            SetStatus(new(ServerState.Stopped, Localization.T("engine.session.stopped")));
        }
        catch (Exception exception) when (acquired)
        {
            SetStatus(new(ServerState.Faulted, exception.Message));
            throw;
        }
        finally
        {
            lock (_sync) _stopRequests--;
            if (acquired) _operation.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
            // Publish one shared disposal task before shutdown can emit another status event.
            _disposeTask ??= Task.Run(() => StopAsync());
            return new ValueTask(_disposeTask);
        }
    }

    private async Task WaitUntilReadyAsync(RunningProcess owned, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_startupTimeout);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { BaseAddress = new Uri($"http://127.0.0.1:{owned.Configuration.Port}/"), Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (owned.Process.HasExited)
                    throw new IOException(Localization.T("engine.session.earlyExit", owned.Process.ExitCode));
                if (TcpProcessOwner.OwnsListener(owned.Configuration.Port, owned.Process.Id))
                {
                    try
                    {
                        using var health = await client.GetAsync("health", timeout.Token).ConfigureAwait(false);
                        if (health.IsSuccessStatusCode)
                        {
                            using var modelsResponse = await client.GetAsync("v1/models", timeout.Token).ConfigureAwait(false);
                            if (modelsResponse.IsSuccessStatusCode)
                            {
                                await using var body = await modelsResponse.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                                using var models = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token).ConfigureAwait(false);
                                if (models.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array &&
                                    data.EnumerateArray().Any(model => model.TryGetProperty("id", out var id) && id.GetString() == owned.Configuration.Model.Definition.ApiModelId) &&
                                    !owned.Process.HasExited && TcpProcessOwner.OwnsListener(owned.Configuration.Port, owned.Process.Id)) return;
                            }
                        }
                    }
                    catch (HttpRequestException) { }
                    catch (JsonException) { }
                    catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }
                }
                await Task.Delay(150, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(Localization.T("engine.session.startupTimeout"));
        }
    }

    private async Task MonitorExitAsync(RunningProcess owned)
    {
        try
        {
            await owned.Process.WaitForExitAsync().ConfigureAwait(false);
            await _operation.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(_owned, owned)) return;
                var code = owned.Process.ExitCode;
                await CleanupOwnedAsync().ConfigureAwait(false);
                SetStatus(new(ServerState.Faulted, Localization.T("engine.session.crashed", code, owned.Log.Path)));
            }
            finally { _operation.Release(); }
        }
        catch (ObjectDisposedException) { }
        catch (Exception exception)
        {
            SetStatus(new(ServerState.Faulted, Localization.T("engine.session.statusFailed", exception.Message)));
        }
    }

    private async Task CleanupOwnedAsync()
    {
        var owned = _owned;
        if (owned is null) return;
        owned.Job.Dispose();
        if (!owned.Process.HasExited)
        {
            // Job termination is asynchronous. Give it a moment before the assignment-failure fallback.
            using var jobTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await owned.Process.WaitForExitAsync(jobTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { owned.Process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (owned.Process.HasExited) { }
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await owned.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                throw new IOException(Localization.T("engine.session.stopTimeout"));
            }
        }
        // Draining both pipes prevents a verbose server from blocking its own shutdown.
        try
        {
            var drains = Task.WhenAll(owned.Output, owned.Error);
            if (await Task.WhenAny(drains, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) != drains)
            {
                owned.Process.StandardOutput.Dispose();
                owned.Process.StandardError.Dispose();
            }
            await drains.ConfigureAwait(false);
        }
        finally
        {
            _owned = null;
            await owned.Log.DisposeAsync().ConfigureAwait(false);
            owned.Process.Dispose();
        }
    }

    private static async Task PumpAsync(StreamReader reader, BoundedLog log, string source)
    {
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
                await log.WriteAsync($"[{source}] " + new string(buffer, 0, count)).ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private void SetStatus(SessionStatus status)
    {
        lock (_sync) _status = status;
        var handlers = StatusChanged;
        if (handlers is null) return;
        foreach (EventHandler<SessionStatus> handler in handlers.GetInvocationList())
        {
            try { handler(this, status); }
            catch { /* A UI subscriber must not break process cleanup. */ }
        }
    }

    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(message, path);
    }

    private static void EnsurePortAvailable(int port)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.ExclusiveAddressUse = true;
        try { listener.Start(); }
        catch (SocketException exception)
        {
            throw new IOException(Localization.T("engine.session.portInUse", port), exception);
        }
    }

    private static void TrimOldLogs(string directory)
    {
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("server-*.log")
                     .OrderByDescending(file => file.LastWriteTimeUtc).Skip(9))
        {
            try { file.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class RunningProcess(Process process, WindowsJob job, BoundedLog log, LaunchConfiguration configuration)
    {
        public Process Process { get; } = process;
        public WindowsJob Job { get; } = job;
        public BoundedLog Log { get; } = log;
        public LaunchConfiguration Configuration { get; } = configuration;
        public Task Output { get; set; } = Task.CompletedTask;
        public Task Error { get; set; } = Task.CompletedTask;
    }

    private sealed class BoundedLog(string path) : IAsyncDisposable
    {
        private const int MaximumBytes = 5 * 1024 * 1024;
        private readonly FileStream _stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true);
        private readonly SemaphoreSlim _write = new(1, 1);
        private long _written;
        private bool _failed;
        public string Path { get; } = path;

        public async Task WriteAsync(string text)
        {
            await _write.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_failed || _written >= MaximumBytes) return;
                var data = Encoding.UTF8.GetBytes(text);
                var count = (int)Math.Min(data.Length, MaximumBytes - _written);
                await _stream.WriteAsync(data.AsMemory(0, count)).ConfigureAwait(false);
                _written += count;
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException) { _failed = true; }
            finally { _write.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            try { await _stream.DisposeAsync().ConfigureAwait(false); }
            catch (IOException) { }
            _write.Dispose();
        }
    }
}
