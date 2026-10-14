using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GemmaLauncher.Core;

/// <summary>One local API/Web UI origin. Presets are captured once per request; response bytes are streamed unchanged.</summary>
public sealed class RequestGateway : IAsyncDisposable
{
    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private WebApplication? _application;
    private CancellationTokenSource? _stop;
    private TaskPreset _preset = TaskPreset.Empty;
    public int PublicPort { get; private set; }
    private static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Host", "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer", "Upgrade", "Proxy-Authorization", "Proxy-Authenticate", "Content-Length" };

    public void UpdatePreset(TaskPreset preset)
    {
        preset.Validate();
        Volatile.Write(ref _preset, preset with { Glossary = preset.Glossary.ToArray() });
    }

    public async Task StartAsync(int publicPort, int backendPort, TaskPreset preset, CancellationToken token = default)
    {
        if (_application is not null) throw new InvalidOperationException("Gateway already started.");
        UpdatePreset(preset);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 32 * 1024 * 1024;
            options.Listen(IPAddress.Loopback, publicPort);
        });
        var app = builder.Build();
        var stop = new CancellationTokenSource();
        app.Run(context => ForwardAsync(context, backendPort, stop.Token));
        try
        {
            await app.StartAsync(token).ConfigureAwait(false);
            PublicPort = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
            _stop = stop; _application = app;
        }
        catch { stop.Dispose(); await app.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task StopAsync()
    {
        var app = _application; var stop = _stop;
        _application = null; _stop = null;
        if (app is null) return;
        stop!.Cancel();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await app.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        finally { await app.DisposeAsync().ConfigureAwait(false); stop.Dispose(); }
    }

    private async Task ForwardAsync(HttpContext context, int backendPort, CancellationToken stopping)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
        var token = cancellation.Token;
        try
        {
            // Reject non-local browser origins; desktop API clients do not send Origin.
            if (context.Request.Host.Host is not ("127.0.0.1" or "localhost") ||
                context.Request.Headers.Origin is { Count: > 0 } origins && origins.Any(origin =>
                    !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme is not ("http" or "https")))
            { context.Response.StatusCode = 403; return; }
            var path = context.Request.Path.Value ?? "/";
            var chat = path is "/v1/chat/completions" or "/chat/completions";
            var completion = path is "/completion" or "/completions" or "/v1/completions";
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
                new Uri($"http://127.0.0.1:{backendPort}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}"));
            if (context.Request.Method == "POST" && (chat || completion))
            {
                if (context.Request.Headers.ContentEncoding.Count > 0) { context.Response.StatusCode = 415; return; }
                var input = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: token).ConfigureAwait(false) as JsonObject
                    ?? throw new InvalidDataException(Localization.T("engine.library.presetRequest"));
                var transformed = Volatile.Read(ref _preset).Apply(input, chat, nativeCompletion: path is "/completion" or "/completions");
                request.Content = new StringContent(transformed.ToJsonString(), Encoding.UTF8, "application/json");
            }
            else if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
                request.Content = new StreamContent(context.Request.Body);
            var excluded = new HashSet<string>(HopHeaders, StringComparer.OrdinalIgnoreCase);
            foreach (var name in context.Request.Headers.Connection.ToString().Split(',', StringSplitOptions.TrimEntries)) excluded.Add(name);
            foreach (var header in context.Request.Headers)
            {
                if (excluded.Contains(header.Key)) continue;
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && request.Content is not null && !request.Content.Headers.Contains(header.Key))
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            context.Response.StatusCode = (int)response.StatusCode;
            var responseExcluded = new HashSet<string>(HopHeaders, StringComparer.OrdinalIgnoreCase);
            foreach (var value in response.Headers.Connection) foreach (var name in value.Split(',', StringSplitOptions.TrimEntries)) responseExcluded.Add(name);
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!responseExcluded.Contains(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[16384]; int read;
            while ((read = await body.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { context.Abort(); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or BadHttpRequestException or HttpRequestException or IOException)
        {
            if (context.Response.HasStarted) { context.Abort(); return; }
            context.Response.Clear();
            context.Response.StatusCode = ex is BadHttpRequestException bad ? bad.StatusCode : ex is JsonException or InvalidDataException ? 400 : 502;
            await context.Response.WriteAsJsonAsync(new { error = new { message = ex is InvalidDataException ? ex.Message : Localization.T("engine.library.gatewayError"), type = "launcher_error" } }, token).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); _client.Dispose(); }
}
