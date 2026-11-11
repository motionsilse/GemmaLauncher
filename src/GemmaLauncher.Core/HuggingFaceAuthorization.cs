using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GemmaLauncher.Core.Accounts;
using Microsoft.AspNetCore.WebUtilities;

namespace GemmaLauncher.Core;

/// <summary>One download authorization for a registered public Hugging Face client.</summary>
public sealed class HuggingFaceAuthorization : IDisposable
{
    private readonly string clientId;
    private readonly HttpClient client;
    private string? credential;
    public bool IsAuthorized => credential is not null;
    public void ClearAuthorization() => credential = null;

    public HuggingFaceAuthorization(string clientId, HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        this.clientId = clientId;
        client = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(20),
            MaxResponseContentBufferSize = 64 * 1024
        };
    }

    public async Task<string> SignInAsync(Action<Uri> openBrowser, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (credential is { } authorized) return authorized;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromMinutes(5));
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        try
        {
            await using var callback = await AccountCallback.StartAsync(state, 0, budget.Token);
            var address = QueryHelpers.AddQueryString("https://huggingface.co/oauth/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = callback.Address.AbsoluteUri,
                ["response_type"] = "code",
                ["scope"] = "gated-repos",
                ["state"] = state,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256"
            });
            openBrowser(new Uri(address));
            var result = await callback.WaitAsync(budget.Token);
            if (result.Declined) throw new IOException(Localization.T("ui.account.loginDeclined"));

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = result.Code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = callback.Address.AbsoluteUri
            });
            using var response = await client.PostAsync("https://huggingface.co/oauth/token", content, budget.Token);
            if (!response.IsSuccessStatusCode) throw new IOException(Localization.T("ui.anis.loginFailed"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(budget.Token));
            if (body.RootElement.ValueKind != JsonValueKind.Object || !body.RootElement.TryGetProperty("access_token", out var value) ||
                value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 4096 } credential ||
                !IsValidAccessToken(credential))
                throw new IOException(Localization.T("ui.anis.loginFailed"));
            this.credential = credential;
            return credential;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException(Localization.T("ui.account.connectionTimeout"));
        }
        catch (JsonException)
        {
            throw new IOException(Localization.T("ui.anis.loginFailed"));
        }
    }

    // RFC 6750 section 2.1: OAuth credentials are opaque bearer values, including JWTs.
    internal static bool IsValidAccessToken(string value)
    {
        if (value.Length is 0 or > 4096 || value[0] == '=') return false;
        var padding = false;
        foreach (var character in value)
        {
            if (character == '=') { padding = true; continue; }
            if (padding || !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '.' or '_' or '~' or '+' or '/')) return false;
        }
        return true;
    }

    public void Dispose() { ClearAuthorization(); client.Dispose(); }
}
