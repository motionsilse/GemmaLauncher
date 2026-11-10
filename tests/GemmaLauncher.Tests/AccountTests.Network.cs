using GemmaLauncher.Core.Accounts;
using System.Net;
using System.Text;
namespace GemmaLauncher.Tests;
public static partial class AccountTests
{
    public static async Task RuntimeSmokeAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "CatLoaderRuntime-" + Guid.NewGuid().ToString("N"));
        using var store = new AccountStateStore(root);
        try
        {
            using var lease = store.AcquireOwnership();
            using var installer = new GemmaLauncher.Core.InstallationService(new(root), discoverExistingModels: false);
            var runtime = new AccountRuntime(store, installer);
            var exe = await runtime.EnsureAsync(null, default);
            Assert(await runtime.EnsureAsync(null, default) == exe, "Reuse verified runtime");
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            await using var host = new AccountHost(store, exe, port);
            await host.StartAsync();
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            using var response = await client.GetAsync($"http://127.0.0.1:{port}/v1/models");
            Assert(response.StatusCode == HttpStatusCode.Unauthorized, "API requires local key");
            client.DefaultRequestHeaders.Authorization = new("Bearer", host.LocalKey);
            using var management = await client.GetAsync($"http://127.0.0.1:{port}/v0/management/auth-files");
            Assert(management.StatusCode == HttpStatusCode.Unauthorized, "API key cannot access management");
            // Attach the actual owned engine to the health monitor without an
            // OAuth account. Only readiness is arranged; exit detection, process
            // cleanup and notifications run through the production classes.
            await using var connection = new AccountConnection(root,port);
            var fields = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(AccountConnection).GetField("host",fields)!.SetValue(connection,host);
            typeof(AccountConnection).GetField("ready",fields)!.SetValue(connection,true);
            ((System.Threading.Timer)typeof(AccountConnection).GetField("monitor",fields)!.GetValue(connection)!).Change(20,20);
            await host.StopAsync();
            var deadline=DateTime.UtcNow.AddSeconds(3);
            while(connection.FailureCode is null && DateTime.UtcNow<deadline) await Task.Delay(20);
            Assert(connection.FailureCode=="runtime_exited" && !connection.IsReady,"Actual engine disappearance must retain an unexpected-exit reason");
            await connection.StopAsync(default);
            Assert(connection.FailureCode is null,"Explicit cleanup acknowledges actual engine exit");
            Assert(!host.IsRunning, "Owned engine stopped");
            Console.WriteLine("Official engine download, reuse, API authentication, management isolation, unexpected-exit notification, stop: PASS (no account or inference)");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
    }
    private static async Task NetworkAsync(string root)
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("models") ? "{\"data\":[{\"id\":\"gpt-6-luna\"},{\"id\":\"gpt-6-luna\"}]}" : "{\"model\":\"gpt-6-luna\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"OK\"}}]}")) { BaseAddress = new("http://127.0.0.1/v1/") };
        var api = new AccountApi(http);
        Assert((await api.ModelsAsync(default)).Count == 1, "Distinct model ids");
        Assert((await api.TestAsync("gpt-6-luna", "low", default)).TextMatched, "Explicit completion probe");
        try { AccountAuthorization.Parse("https://evil.invalid/oauth/authorize", new string('a', 32)); throw new Exception("Unsafe authorization accepted"); }
        catch (ConnectionFailure error) when (error.Code == "invalid_login_url") { }
        using var large = new HttpClient(new Handler(_ => new string('x', 1_048_577))) { BaseAddress = http.BaseAddress };
        try { await new AccountApi(large).ModelsAsync(default); throw new Exception("Oversized response accepted"); }
        catch (ConnectionFailure error) when (error.Code == "proxy_response_too_large") { }
        await using var untouched = new AccountConnection(Path.Combine(root, "unused"));
        Assert(!untouched.IsReady && !untouched.IsRunning && !Directory.Exists(Path.Combine(root, "unused")), "Lazy connection must have no writes");
        var state = new string('a',32);
        await using var callback = await AccountCallback.StartAsync(state,0);
        using var callbackHttp = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        using var invalid = await callbackHttp.GetAsync(callback.Address + "?state=wrong&code=fixture");
        Assert(invalid.StatusCode == HttpStatusCode.BadRequest, "Invalid state must not complete callback");
        using var accepted = await callbackHttp.GetAsync(callback.Address + "?state=" + state + "&code=fixture");
        Assert(accepted.IsSuccessStatusCode && (await callback.WaitAsync(default)).Code == "fixture", "Accept matching state once");
        using var duplicate = await callbackHttp.GetAsync(callback.Address + "?state=" + state + "&code=fixture");
        Assert(duplicate.StatusCode == HttpStatusCode.Conflict, "Reject repeated callback");
        var brokenRoot = Path.Combine(root,"bad-runtime"); using var store = new AccountStateStore(brokenRoot); using var ownership = store.AcquireOwnership();
        var brokenExe = Path.Combine(brokenRoot,"bad.exe"); File.WriteAllBytes(brokenExe,[1,2,3]);
        await using var badHost = new AccountHost(store,brokenExe,43219);
        try { await badHost.StartAsync(); throw new Exception("Corrupt executable accepted"); }
        catch (ConnectionFailure failure) when (failure.Code == "runtime_checksum_mismatch") { }
        using var usageClient = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("auth-files")
            ? "{\"files\":[{\"type\":\"codex\",\"auth_index\":\"fake-index\",\"id_token\":{\"chatgpt_account_id\":\"fake-workspace\"}}]}"
            : System.Text.Json.JsonSerializer.Serialize(new { status_code = 200, body = "{\"rate_limit\":{\"primary_window\":{\"used_percent\":12}}}" }))) { BaseAddress = new("http://127.0.0.1/v0/management/") };
        var usage = await new AccountUsageClient(usageClient).ReadAsync(default);
        Assert(usage.Windows.Count == 1 && usage.Windows[0].UsedPercent == 12 && usage.Windows[0].ResetsAt is null && usage.Windows[0].DurationSeconds is null, "Preserve missing quota fields without estimates");
    }
}
