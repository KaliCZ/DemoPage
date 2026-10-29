namespace Kalandra.Infrastructure.Turnstile;

public sealed class AlwaysPassTurnstileValidator : ITurnstileValidator
{
    public Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken ct = default)
        => Task.FromResult(true);
}
