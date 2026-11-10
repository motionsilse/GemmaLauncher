using GemmaLauncher.Core.Accounts;
using System.Security.Cryptography;
using System.Text;

namespace GemmaLauncher.Tests;
public static partial class AccountTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "CatLoaderTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AccountStateStore(root);
            Assert(store.ReadSummary().State == SavedAccountState.Missing && !Directory.Exists(root), "Read must not create state");
            string key;
            using (store.AcquireOwnership())
            {
                key = store.ReadOrCreateLocalKey();
                Assert(key.Length == 43, "Legacy key format");
                var original = File.ReadAllBytes(Path.Combine(root, "local-key.dpapi"));
                Assert(store.ReadOrCreateLocalKey() == key && original.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "local-key.dpapi"))), "Keep key bytes");
                using var other = new AccountStateStore(root);
                try { using var unexpected = other.AcquireOwnership(); throw new Exception("Lock was not exclusive"); }
                catch (ConnectionFailure error) when (error.Code == "session_in_use") { }
                Directory.CreateDirectory(Path.Combine(root, "auth"));
                File.WriteAllText(Path.Combine(root, "auth", "codex-test.json"), "{\"type\":\"codex\",\"email\":\"fake@example.invalid\",\"plan_type\":\"test\"}");
                Assert(store.ReadSummary().State == SavedAccountState.Available, "Read saved account");
                File.WriteAllText(Path.Combine(root, "auth", "other.json"), "{\"type\":\"other\"}");
                store.ClearLocalLogin();
                Assert(File.Exists(Path.Combine(root, "auth", "other.json")) && store.ReadSummary().State == SavedAccountState.Missing, "Only remove codex account");
                File.WriteAllText(Path.Combine(root, "auth", "broken.json"), "{");
                Assert(store.ReadSummary().State == SavedAccountState.Unreadable, "Corrupt is not missing");
            }
            File.WriteAllBytes(Path.Combine(root, "local-key.dpapi"), [1, 2, 3]);
            using (store.AcquireOwnership())
            {
                try { store.ReadOrCreateLocalKey(); throw new Exception("Corrupt key replaced"); }
                catch (ConnectionFailure error) when (error.Code == "local_key_unreadable") { }
                Assert(File.ReadAllBytes(Path.Combine(root, "local-key.dpapi")).SequenceEqual(new byte[] {1,2,3}), "Corrupt key retained");
            }
            await Task.CompletedTask;
            await NetworkAsync(root);
            await HealthNotificationAsync(root);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static async Task HealthNotificationAsync(string root)
    {
        await using var connection = new AccountConnection(Path.Combine(root,"health"));
        using var notified = new ManualResetEventSlim();
        connection.StatusChanged += (_,_) => notified.Set();
        // Simulate the exact state seen by the health poll after the owned process
        // disappears. No credentials, downloader or external service are needed.
        typeof(AccountConnection).GetField("ready",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(connection,true);
        var monitor = (System.Threading.Timer)typeof(AccountConnection).GetField("monitor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(connection)!;
        monitor.Change(0,Timeout.Infinite);
        Assert(notified.Wait(TimeSpan.FromSeconds(3)),"Health poll must notify after unexpected exit");
        string? Failure() => typeof(AccountConnection).GetProperty("FailureCode")?.GetValue(connection) as string;
        Assert(!connection.IsReady && Failure()=="runtime_exited","Health poll must retain the unexpected exit cause");
        await connection.StopAsync(default);
        Assert(Failure() is null,"Explicit stop must acknowledge the exit cause");
        notified.Reset(); monitor.Change(0,Timeout.Infinite);
        Assert(!notified.Wait(TimeSpan.FromMilliseconds(100)),"A normal stopped state must not trigger an exit failure");
    }
}
