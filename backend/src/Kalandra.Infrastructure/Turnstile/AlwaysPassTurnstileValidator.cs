namespace Kalandra.Infrastructure.Turnstile;

// Skips the Cloudflare Turnstile round-trip and always returns true.
// Used by integration tests and by the API in Development so dev works
// offline.
public sealed class AlwaysPassTurnstileValidator : ITurnstileValidator
{
    public Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken ct = default)
        => Task.FromResult(true);
}
