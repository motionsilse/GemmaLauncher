// Adapted from motionsilse/CatRoute c5c74ad, product core (MIT).
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace GemmaLauncher.Core.Accounts;

public sealed class AccountApi(HttpClient client)
{
    private readonly HttpClient client = client;
    public async Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "models");
        using var body = await SendAsync(client, request, ct);
        try
        {
            var result = body.RootElement.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToArray();
            if (result.Any(string.IsNullOrWhiteSpace)) throw new ConnectionFailure("invalid_model_catalog");
            return result.Select(m => m!).Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException)
        { throw new ConnectionFailure("invalid_model_catalog"); }
    }

    public async Task<ConnectionProbeResult> TestAsync(string model, string effort, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model) || effort is not ("low" or "medium" or "none")) throw new ConnectionFailure("invalid_test_options");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10));
        var timer = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(new { model, reasoning_effort = effort, stream = false,
                messages = new[] { new { role = "user", content = "Reply with exactly OK." } } })
        };
        using var body = await SendAsync(client, request, budget.Token);
        try
        {
            var choice = body.RootElement.GetProperty("choices")[0];
            if (choice.GetProperty("finish_reason").GetString() != "stop") throw new ConnectionFailure("response_not_completed");
            var text = choice.GetProperty("message").GetProperty("content").GetString();
            var reported = body.RootElement.GetProperty("model").GetString();
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(reported)) throw new ConnectionFailure("invalid_completion");
            return new ConnectionProbeResult(text.Trim() == "OK", reported, timer.Elapsed.TotalMilliseconds);
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        { throw new ConnectionFailure("invalid_completion"); }
    }

    internal static async Task<JsonDocument> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
        // HttpClient.Timeout stops at the headers with ResponseHeadersRead.
        // Keep the same deadline alive while reading the body as well.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(http.Timeout == Timeout.InfiniteTimeSpan ? TimeSpan.FromSeconds(15) : http.Timeout);
        var token = budget.Token;
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new ConnectionFailure("proxy_http_error", (int)response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream(); var chunk = new byte[8192];
        int length;
        while ((length = await stream.ReadAsync(chunk, token)) != 0)
        {
            if (buffer.Length + length > 1_048_576) throw new ConnectionFailure("proxy_response_too_large");
            buffer.Write(chunk, 0, length);
        }
        try { return JsonDocument.Parse(buffer.ToArray()); }
        catch (JsonException) { throw new ConnectionFailure("invalid_proxy_json"); }
    }
}
