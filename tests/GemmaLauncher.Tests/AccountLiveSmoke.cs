using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using GemmaLauncher.Core.Accounts;

namespace GemmaLauncher.Tests;

/// <summary>Opt-in real OAuth test in a new account directory; no computer/browser automation.</summary>
internal static class AccountLiveSmoke
{
    public static async Task RunAsync(string directory)
    {
        var root = Path.GetFullPath(directory);
        if (Directory.Exists(root)) throw new InvalidOperationException("Live test requires a new, nonexistent directory.");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await using var connection = new AccountConnection(root, port);
        var progress = new StageProgress();
        var report = new Dictionary<string, object> { ["runtime"] = AccountRuntime.Version, ["startedAt"] = DateTimeOffset.UtcNow };
        try
        {
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                try
                {
                    await connection.LoginAsync(_ => cancel.Cancel(), "ko", progress, cancel.Token);
                    throw new Exception("Cancelled sign-in unexpectedly succeeded");
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            }
            Assert(!connection.IsRunning && connection.ReadSavedAccount().State == SavedAccountState.Missing, "Cancellation must stop the helper and leave no saved account");
            report["cancelBeforeAuthorization"] = true;
            Console.WriteLine("CANCELLATION PASS. Opening the normal ChatGPT sign-in page. Complete sign-in in the browser; no automation is used.");
            await connection.LoginAsync(uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }), "ko", progress, lifetime.Token);
            Assert(!connection.IsRunning && connection.ReadSavedAccount().State == SavedAccountState.Available, "Login must save the account without starting the user API");
            report["realOAuthLogin"] = true;
            Console.WriteLine("LOGIN PASS. Saved account verified; starting the API.");
            await connection.ConnectAsync(progress, lifetime.Token);
            Assert(connection.IsReady, "API must become ready");
            var models = await connection.ModelsAsync(lifetime.Token);
            Assert(models.Contains(AccountConnection.DefaultModel), "The offered model must actually be listed");
            report["modelAvailable"] = true;
            using (var unauthenticated = new HttpClient(new SocketsHttpHandler { UseProxy = false }))
            using (var response = await unauthenticated.GetAsync(connection.ApiAddress + "/models", lifetime.Token))
                Assert(response.StatusCode == HttpStatusCode.Unauthorized, "The user API must require its local key");
            var usage = await connection.UsageAsync(lifetime.Token);
            report["usageWindows"] = usage.Windows.Count;
            var probe = await connection.TestAsync(AccountConnection.DefaultModel, lifetime.Token);
            Assert(probe.TextMatched, "Real inference must return the requested OK");
            report["inferenceMs"] = probe.ElapsedMs;
            report["reportedModel"] = probe.ReportedModel;
            var localKey = connection.LocalApiKey;
            await connection.StopAsync(default);
            Assert(!connection.IsRunning && connection.ReadSavedAccount().State == SavedAccountState.Available, "Stopping must keep sign-in");
            await connection.ConnectAsync(progress, lifetime.Token);
            Assert(connection.IsReady && connection.LocalApiKey == localKey, "Restart must reuse sign-in and the local API key");
            Assert((await connection.TestAsync(AccountConnection.DefaultModel, lifetime.Token)).TextMatched, "Reconnected inference must work without another login");
            report["restartWithoutLogin"] = true;
            await connection.SignOutAsync(lifetime.Token);
            Assert(!connection.IsRunning && connection.ReadSavedAccount().State == SavedAccountState.Missing, "Sign-out must stop the server and remove the test login");
            Assert(!Directory.EnumerateFiles(Path.Combine(root, "auth"), "codex-*.json*").Any()
                && !Directory.Exists(Path.Combine(root, "auth", ".codex-refresh")), "Sign-out must remove credential files, identity locks and renewal receipts");
            try { await connection.ConnectAsync(progress, lifetime.Token); throw new Exception("Signed-out connection accepted"); }
            catch (ConnectionFailure failure) when (failure.Code == "login_required") { }
            report["signOut"] = true;
            report["result"] = "PASS";
            Console.WriteLine("PASS: real OAuth, model list, usage, inference, stop, reconnect, inference again, sign-out.");
        }
        catch (Exception error)
        {
            report["result"] = "FAIL";
            report["failure"] = error is ConnectionFailure failure ? failure.Code : error.GetType().Name;
            throw;
        }
        finally
        {
            await connection.StopAsync(default);
            if (connection.ReadSavedAccount().State == SavedAccountState.Available) await connection.SignOutAsync(default);
            if (Directory.Exists(root))
                await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void Assert(bool success, string message) { if (!success) throw new Exception(message); }
    private sealed class StageProgress : IProgress<ConnectionProgress>
    {
        private AccountPreparationStage? last;
        public void Report(ConnectionProgress value)
        {
            if (last == value.Stage) return;
            last = value.Stage; Console.WriteLine("STAGE: " + value.Stage);
        }
    }
}
