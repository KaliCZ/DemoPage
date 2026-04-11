using Kalandra.Infrastructure.Turnstile;

namespace Kalandra.Api.IntegrationTests.Helpers;

public class AlwaysPassTurnstileValidator : ITurnstileValidator
{
    public Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken ct = default)
        => Task.FromResult(true);
}
