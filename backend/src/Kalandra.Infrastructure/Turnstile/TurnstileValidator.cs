using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Kalandra.Infrastructure.Configuration;

namespace Kalandra.Infrastructure.Turnstile;

public sealed class TurnstileValidator(HttpClient httpClient, TurnstileConfig config) : ITurnstileValidator
{
    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public async Task<bool> ValidateAsync(string token, string? remoteIp, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, string>
        {
            ["secret"] = config.SecretKey,
            ["response"] = token
        };

        if (!string.IsNullOrEmpty(remoteIp))
            payload["remoteip"] = remoteIp;

        var response = await httpClient.PostAsync(
            VerifyUrl,
            new FormUrlEncodedContent(payload),
            ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(ct);
        return result?.Success == true;
    }

    private sealed record TurnstileResponse(
        [property: JsonPropertyName("success")] bool Success);
}
