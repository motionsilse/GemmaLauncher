// Adapted from motionsilse/CatRoute c5c74ad, product core. See THIRD-PARTY-NOTICES.md.
using System.Net.Http.Json;
using System.Text.Json;

namespace GemmaLauncher.Core.Accounts;


/// <summary>Read account limits through the engine's existing authentication, without inference or token export.</summary>
public sealed class AccountUsageClient(HttpClient management)
{
    public async Task<AccountUsage> ReadAsync(CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        using var accountRequest = new HttpRequestMessage(HttpMethod.Get, "auth-files");
        using var accountDocument = await AccountApi.SendAsync(management, accountRequest, budget.Token);
        var files = Get(accountDocument.RootElement, "files");
        if (files.ValueKind != JsonValueKind.Array) throw new ConnectionFailure("usage_account_unavailable");
        var accounts = files.EnumerateArray().Where(file => Text(file, "type") == "codex" || Text(file, "provider") == "codex").ToArray();
        if (accounts.Length != 1) throw new ConnectionFailure("usage_account_unavailable");
        var index = Text(accounts[0], "auth_index");
        if (string.IsNullOrWhiteSpace(index)) throw new ConnectionFailure("usage_account_unavailable");
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer $TOKEN$", ["Accept"] = "application/json",
            ["Cache-Control"] = "no-cache", ["Pragma"] = "no-cache"
        };
        // auth_index selects the credential; the upstream header selects its workspace.
        // The pinned engine exposes decoded claims here, not the raw ID token.
        var accountId = Text(Get(accounts[0], "id_token"), "chatgpt_account_id");
        if (!string.IsNullOrWhiteSpace(accountId)) headers["Chatgpt-Account-Id"] = accountId.Trim();
        using var request = new HttpRequestMessage(HttpMethod.Post, "api-call")
        {
            Content = JsonContent.Create(new
            {
                auth_index = index, method = "GET", url = "https://chatgpt.com/backend-api/wham/usage",
                header = headers
            })
        };
        using var envelope = await AccountApi.SendAsync(management, request, budget.Token);
        var status = Integer(envelope.RootElement, "status_code");
        if (status is not (>= 100 and <= 599)) throw new ConnectionFailure("usage_unavailable");
        if (status != 200) throw new ConnectionFailure("usage_http_error", (int)status);
        try
        {
            using var document = JsonDocument.Parse(Text(envelope.RootElement, "body") ?? "");
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new ConnectionFailure("usage_unavailable");
            var windows = new List<UsageWindow>();
            AddWindows(Get(root, "rate_limit"), "Codex", windows);
            var additional = Get(root, "additional_rate_limits");
            if (additional.ValueKind == JsonValueKind.Array)
                foreach (var item in additional.EnumerateArray().Take(16))
                {
                    var name = Text(item, "limit_name") ?? Text(item, "metered_feature") ?? "Codex";
                    name = new string(name.Where(character => !char.IsControl(character)).Take(80).ToArray());
                    AddWindows(Get(item, "rate_limit"), string.IsNullOrWhiteSpace(name) ? "Codex" : name, windows);
                }
            return new AccountUsage(DateTimeOffset.UtcNow, windows.AsReadOnly());
        }
        catch (JsonException) { throw new ConnectionFailure("usage_unavailable"); }
    }

    private static void AddWindows(JsonElement rate, string bucket, List<UsageWindow> windows)
    {
        foreach (var name in new[] { "primary_window", "secondary_window" })
        {
            var window = Get(rate, name);
            if (window.ValueKind != JsonValueKind.Object) continue;
            var percent = Get(window, "used_percent");
            double? used = percent.ValueKind == JsonValueKind.Number && percent.TryGetDouble(out var number) && number is >= 0 and <= 100 ? number : null;
            var duration = Integer(window, "limit_window_seconds");
            var reset = Integer(window, "reset_at");
            DateTimeOffset? resetsAt = reset is > 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(reset.Value) : null;
            windows.Add(new UsageWindow(bucket, used, duration is > 0 ? duration : null, resetsAt));
        }
    }

    private static JsonElement Get(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) ? result : default;
    private static string? Text(JsonElement value, string name) =>
        Get(value, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    private static long? Integer(JsonElement value, string name) =>
        Get(value, name) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt64(out var result) ? result : null;
}
