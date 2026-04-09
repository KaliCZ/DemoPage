namespace Kalandra.Infrastructure.Turnstile;

public interface ITurnstileValidator
{
    Task<bool> ValidateAsync(string? token, string? remoteIp, CancellationToken ct = default);
}
