using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace GemmaLauncher.Core.Accounts;

public sealed class AccountStateStore(string directory) : IDisposable
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string AuthDirectory => Path.Combine(DirectoryPath, "auth");
    private FileStream? ownership;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CatRoute.OpenAiProxy.LocalKey.v1");

    internal void CheckPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full != DirectoryPath && !full.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ConnectionFailure("unsafe_state_path");
        for (var item = full; item is not null; item = Path.GetDirectoryName(item))
            if (Path.Exists(item) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new ConnectionFailure("unsafe_state_path");
    }

    public AccountSummary ReadSummary()
    {
        try
        {
            CheckPath(AuthDirectory);
            if (!Directory.Exists(AuthDirectory)) return new(SavedAccountState.Missing);
            var accounts = ReadAccounts();
            return accounts.Count switch { 0 => new(SavedAccountState.Missing), 1 => accounts[0].Summary, _ => new(SavedAccountState.Multiple) };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ConnectionFailure)
        { return new(SavedAccountState.Unreadable); }
    }

    private List<(string File, AccountSummary Summary)> ReadAccounts()
    {
        var accounts = new List<(string, AccountSummary)>();
        foreach (var file in Directory.EnumerateFiles(AuthDirectory, "*.json"))
        {
            CheckPath(file);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 1_048_576) throw new ConnectionFailure("account_unreadable");
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "codex") continue;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            accounts.Add((file, new(SavedAccountState.Available, Text("email"), Text("plan_type") ?? "unknown")));
        }
        return accounts;
    }

    public IDisposable AcquireOwnership()
    {
        if (ownership is not null) throw new ConnectionFailure("session_in_use");
        CheckPath(DirectoryPath);
        var info = Directory.CreateDirectory(DirectoryPath);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(acl);
        CheckPath(Path.Combine(DirectoryPath, "session.lock"));
        try { ownership = new FileStream(Path.Combine(DirectoryPath, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new ConnectionFailure("session_in_use"); }
        return new Lease(this);
    }
    private sealed class Lease(AccountStateStore owner) : IDisposable { public void Dispose() { owner.ownership?.Dispose(); owner.ownership = null; } }
    internal void RequireOwnership() { if (ownership is null) throw new ConnectionFailure("ownership_required"); CheckPath(DirectoryPath); }
    public string ReadOrCreateLocalKey()
    {
        RequireOwnership();
        var file = Path.Combine(DirectoryPath, "local-key.dpapi"); CheckPath(file);
        try
        {
            if (!File.Exists(file))
            {
                var plain = Encoding.UTF8.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_'));
                try { AtomicWrite(file, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            var decoded = ProtectedData.Unprotect(File.ReadAllBytes(file), Entropy, DataProtectionScope.CurrentUser);
            try { var key = Encoding.UTF8.GetString(decoded); if (key.Length != 43) throw new ConnectionFailure("local_key_unreadable"); return key; }
            finally { CryptographicOperations.ZeroMemory(decoded); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException) { throw new ConnectionFailure("local_key_unreadable"); }
    }
    internal void AtomicWrite(string file, byte[] bytes)
    {
        RequireOwnership(); CheckPath(file);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); } File.Move(temporary, file, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void ClearLocalLogin()
    {
        RequireOwnership(); CheckPath(AuthDirectory);
        if (!Directory.Exists(AuthDirectory)) return;
        var accounts = ReadAccounts();
        if (accounts.Count > 1) throw new ConnectionFailure("multiple_accounts");
        // CLIProxyAPI 8.0.66 keeps rotated credentials outside the account JSON.
        // The caller has stopped the owned engine and holds the directory lease.
        // Check every path before deleting; never follow a junction/symlink.
        var files = accounts.Select(account => account.File + ".lock").ToList();
        var directories = new List<string>();
        var journal = Path.Combine(AuthDirectory, ".codex-refresh");
        CheckPath(journal);
        if (Directory.Exists(journal)) directories.Add(journal);
        for (var i = 0; i < directories.Count; i++)
        {
            CheckPath(directories[i]);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directories[i]))
            {
                CheckPath(entry);
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) directories.Add(entry);
                else files.Add(entry);
            }
        }
        foreach (var file in files) CheckPath(file);
        foreach (var file in files) { CheckPath(file); File.Delete(file); }
        for (var i = directories.Count - 1; i >= 0; i--) { CheckPath(directories[i]); Directory.Delete(directories[i]); }
        // Keep the saved account visible if cleanup failed, so logout can be retried.
        foreach (var account in accounts) { CheckPath(account.File); File.Delete(account.File); }
    }
    public void Dispose() { ownership?.Dispose(); ownership = null; }
}
