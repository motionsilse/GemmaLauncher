using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using GemmaLauncher.Core.Accounts;

namespace GemmaLauncher.Tests;

public static partial class AccountTests
{
    private static async Task LoginFlowAsync()
    {
        using var browserHttp = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        foreach (var outcome in new[] { "ok", "declined", "exchange_failed", "not_saved", "cancel" })
        {
            using var handler = new LoginHandler(outcome);
            using var management = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1/v0/management/") };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task? browser = null;
            var login = new AccountLogin(management, () => new(handler.Saved ? SavedAccountState.Available : SavedAccountState.Missing));
            try
            {
                await login.LoginAsync(uri =>
                {
                    Assert(uri.Host == "auth.openai.com", "Open only the validated authorization URL");
                    if (outcome == "cancel") { deadline.Cancel(); return; }
                    browser = CallbackAsync();
                    async Task CallbackAsync()
                    {
                        var query = outcome == "declined" ? "error=access_denied" : "code=fixture-code";
                        using var response = await browserHttp.GetAsync("http://localhost:1455/auth/callback?state=" + handler.State + "&" + query, deadline.Token);
                        Assert(response.IsSuccessStatusCode, "Browser callback must be accepted");
                    }
                }, deadline.Token);
                Assert(outcome == "ok" && handler.Saved, "Sign-in requires a saved account, not just an OAuth redirect");
            }
            catch (ConnectionFailure error)
            {
                var expected = outcome switch { "declined" => "login_declined", "exchange_failed" => "login_failed", "not_saved" => "login_not_saved", _ => "unexpected" };
                Assert(error.Code == expected, "Preserve the sign-in failure: " + outcome + "/" + error.Code);
            }
            catch (OperationCanceledException) when (outcome == "cancel" && deadline.IsCancellationRequested) { }
            finally { if (browser is not null) await browser; }
            Assert(handler.Cancelled == (outcome != "ok"), "Failed/cancelled login must delete its pending OAuth session");
            Assert(handler.Forwarded == (outcome is "ok" or "exchange_failed" or "not_saved"), "Only an accepted code may reach the token exchange");
        }

        // Both loopback families must be busy: Kestrel can otherwise bind the free family.
        using var occupiedV4 = new TcpListener(IPAddress.Loopback, 1455);
        using var occupiedV6 = new TcpListener(IPAddress.IPv6Loopback, 1455);
        occupiedV4.Server.ExclusiveAddressUse = true; occupiedV6.Server.ExclusiveAddressUse = true;
        occupiedV4.Start(); occupiedV6.Start();
        using var busyHandler = new LoginHandler("ok");
        using var busyHttp = new HttpClient(busyHandler) { BaseAddress = new("http://127.0.0.1/v0/management/") };
        try
        {
            await new AccountLogin(busyHttp, () => new(SavedAccountState.Missing)).LoginAsync(_ => throw new Exception("Do not open a browser without a callback listener"), default);
            throw new Exception("Busy login callback port was accepted");
        }
        catch (ConnectionFailure error) when (error.Code == "login_port_in_use") { }
        Assert(busyHandler.Cancelled, "A callback bind failure must cancel the pending upstream session");
    }

    private sealed class LoginHandler(string outcome) : HttpMessageHandler
    {
        public string State { get; } = Guid.NewGuid().ToString("N");
        public bool Saved, Cancelled, Forwarded;
        private int polls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            object body;
            switch (request.RequestUri!.AbsolutePath.Split('/').Last())
            {
                case "codex-auth-url":
                    body = new { url = "https://auth.openai.com/oauth/authorize?state=" + State + "&redirect_uri=http%3A%2F%2Flocalhost%3A1455%2Fauth%2Fcallback", state = State };
                    break;
                case "oauth-callback":
                    using (var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)))
                    {
                        Assert(request.Method == HttpMethod.Post && payload.RootElement.GetProperty("provider").GetString() == "codex", "Use the upstream Codex callback contract");
                        Assert(payload.RootElement.GetProperty("state").GetString() == State && payload.RootElement.GetProperty("code").GetString() == "fixture-code", "Forward the verified state and code");
                    }
                    Forwarded = true; body = new { status = "ok" }; break;
                case "get-auth-status":
                    Assert(Forwarded && request.RequestUri.Query == "?state=" + State, "Poll only this login attempt");
                    var status = polls++ == 0 ? "wait" : outcome == "exchange_failed" ? "error" : "ok";
                    Saved = status == "ok" && outcome != "not_saved";
                    body = new { status }; break;
                case "oauth-session":
                    Assert(request.Method == HttpMethod.Delete && request.RequestUri.Query == "?state=" + State, "Cancel only this pending attempt");
                    Cancelled = true; body = new { status = "ok" }; break;
                default: throw new Exception("Unexpected management request");
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
    }
}
