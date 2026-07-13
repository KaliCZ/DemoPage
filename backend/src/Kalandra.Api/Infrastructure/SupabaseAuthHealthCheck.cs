using Kalandra.Infrastructure.Tasks;
using Kalandra.Infrastructure.Users;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Verifies that the Supabase Auth admin API is reachable with the configured
/// service key, catching a missing or revoked key at /health rather than as a
/// runtime 401 the first time a user touches auth-admin functionality. Failures
/// report Degraded, not Unhealthy: auth-admin breaks while the rest of the API
/// keeps serving, so readiness is partial, not down.
///
/// Exercises the same <see cref="IUserInfoService"/> production uses for
/// fetching user info, so the health check covers the exact client wiring.
/// </summary>
internal sealed class SupabaseAuthHealthCheck(IUserInfoService userInfoService) : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            // Gotrue's admin client ignores cancellation, so the timeout is enforced here, not via the check registration.
            await userInfoService.PingAsync(ct).WaitObservedAsync(ProbeTimeout, ct);
            return HealthCheckResult.Healthy("Supabase Auth admin API reachable with the configured service key.");
        }
        catch (TimeoutException ex)
        {
            return HealthCheckResult.Degraded($"Supabase Auth admin API did not respond within {ProbeTimeout.TotalSeconds:0}s.", ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded(
                "Supabase Auth admin API rejected the request. Verify Supabase:ProjectUrl and Supabase:ServiceKey.",
                ex);
        }
    }
}
