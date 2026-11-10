// Adapted from motionsilse/CatRoute c5c74ad, product core. See THIRD-PARTY-NOTICES.md.
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace GemmaLauncher.Core.Accounts;

public sealed record AccountAuthorization(Uri BrowserUri, string State, int CallbackPort)
{
    public static AccountAuthorization Parse(string url, string state)
    {
        if (state.Length is < 16 or > 128 || !state.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ||
            !Uri.TryCreate(url, UriKind.Absolute, out var browser) || browser.Scheme != "https" ||
            browser.Host != "auth.openai.com" || browser.Port != 443 || browser.UserInfo != "" ||
            browser.AbsolutePath != "/oauth/authorize" || browser.Fragment != "") throw new ConnectionFailure("invalid_login_url");
        var query = QueryHelpers.ParseQuery(browser.Query);
        if (!query.TryGetValue("state", out var states) || states.Count != 1 || states[0] != state ||
            !query.TryGetValue("redirect_uri", out var redirects) || redirects.Count != 1 ||
            !Uri.TryCreate(redirects[0], UriKind.Absolute, out var redirect) || redirect.Scheme != "http" ||
            redirect.Host is not ("localhost" or "127.0.0.1") || redirect.Port != 1455 ||
            redirect.AbsolutePath != "/auth/callback" || redirect.Query != "" || redirect.Fragment != "" ||
            redirect.UserInfo != "") throw new ConnectionFailure("invalid_login_url");
        return new AccountAuthorization(browser, state, redirect.Port);
    }
}

public sealed record AccountCallbackResult(string Code, bool Declined);

public sealed class AccountCallback : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly TaskCompletionSource<AccountCallbackResult> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Uri Address { get; private set; } = null!;
    private AccountCallback(WebApplication app) => this.app = app;

    public static async Task<AccountCallback> StartAsync(string state, int port, CancellationToken ct = default, string? uiLanguage = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxRequestLineSize = 16384;
            if (port == 0) server.Listen(IPAddress.Loopback, 0);
            else server.ListenLocalhost(port);
        });
        var app = builder.Build(); var callback = new AccountCallback(app);
        app.Run(async context =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; frame-ancestors 'none'";
            if (context.Request.Method != "GET" || context.Request.Path != "/auth/callback" ||
                context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]"))
            { context.Response.StatusCode = 400; return; }
            var query = context.Request.Query;
            var incoming = query["state"]; var code = query["code"]; var error = query["error"];
            if (incoming.Count != 1 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(incoming[0] ?? ""), Encoding.UTF8.GetBytes(state)) ||
                code.Count > 1 || error.Count > 1 ||
                (string.IsNullOrWhiteSpace(code.ToString()) == string.IsNullOrWhiteSpace(error.ToString())))
            { context.Response.StatusCode = 400; return; }
            if (!callback.received.TrySetResult(new AccountCallbackResult(code.ToString(), !string.IsNullOrWhiteSpace(error.ToString()))))
            { context.Response.StatusCode = 409; return; }
            context.Response.ContentType = "text/html; charset=utf-8";
            var language = Localization.Current.LanguageCode;
            var heading = WebUtility.HtmlEncode(Localization.T("ui.account.callbackHeading"));
            var message = WebUtility.HtmlEncode(Localization.T("ui.account.callbackMessage"));
            var close = WebUtility.HtmlEncode(Localization.T("ui.account.callbackClose"));
            await context.Response.WriteAsync($"<!doctype html><html lang=\"{language}\"><meta charset=\"utf-8\"><title>Cat Loader</title><body style=\"font:18px sans-serif;padding:48px\"><h1>{heading}</h1><p>{message}</p><p>{close}</p></body></html>");
        });
        try
        {
            await app.StartAsync(ct);
            callback.Address = new Uri(new Uri(app.Urls.First()), "/auth/callback");
            return callback;
        }
        catch { await app.DisposeAsync(); throw; }
    }

    public Task<AccountCallbackResult> WaitAsync(CancellationToken ct) => received.Task.WaitAsync(ct);
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await app.StopAsync(timeout.Token); }
        finally { await app.DisposeAsync(); }
    }
}

public sealed class AccountLogin(HttpClient management, Func<AccountSummary> readAccount)
{
    public async Task LoginAsync(Action<Uri> openBrowser, CancellationToken ct, string? uiLanguage = null)
    {
        if (readAccount().State != SavedAccountState.Missing) throw new ConnectionFailure("account_already_saved");
        string? state = null; var completed = false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "codex-auth-url");
            using var body = await AccountApi.SendAsync(management, request, budget.Token);
            AccountAuthorization auth;
            try
            {
                auth = AccountAuthorization.Parse(body.RootElement.GetProperty("url").GetString() ?? "",
                    body.RootElement.GetProperty("state").GetString() ?? "");
            }
            catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException)
            { throw new ConnectionFailure("invalid_login_url"); }
            state = auth.State;
            await using var callback = await AccountCallback.StartAsync(state, auth.CallbackPort, budget.Token, uiLanguage);
            openBrowser(auth.BrowserUri);
            var result = await callback.WaitAsync(budget.Token);
            if (result.Declined) throw new ConnectionFailure("login_declined");
            using var forward = new HttpRequestMessage(HttpMethod.Post, "oauth-callback")
            { Content = JsonContent.Create(new { provider = "codex", state, code = result.Code }) };
            using var accepted = await AccountApi.SendAsync(management, forward, budget.Token);
            while (true)
            {
                using var poll = new HttpRequestMessage(HttpMethod.Get, "get-auth-status?state=" + Uri.EscapeDataString(state));
                using var status = await AccountApi.SendAsync(management, poll, budget.Token);
                var value = status.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                if (value == "ok")
                {
                    if (readAccount().State != SavedAccountState.Available) throw new ConnectionFailure("login_not_saved");
                    completed = true; return;
                }
                if (value != "wait") throw new ConnectionFailure("login_failed");
                await Task.Delay(350, budget.Token);
            }
        }
        finally
        {
            if (!completed)
            {
                try
                {
                    if (state is not null)
                    {
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        using var cancel = new HttpRequestMessage(HttpMethod.Delete, "oauth-session?state=" + Uri.EscapeDataString(state));
                        using var ignored = await management.SendAsync(cancel, cleanup.Token);
                    }
                }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { }
            }
        }
    }
}
