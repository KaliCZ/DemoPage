namespace Kalandra.Infrastructure.Turnstile;

// No-op validator for integration tests and Development; dev works offline.
public sealed class AlwaysPassTurnstileValidator : ITurnstileValidator
{
    public Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken ct = default)
        => Task.FromResult(true);
}
