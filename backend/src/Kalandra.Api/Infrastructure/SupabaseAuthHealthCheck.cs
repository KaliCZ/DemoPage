using Kalandra.Infrastructure.Users;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Verifies that the Supabase Auth admin API is reachable with the configured
/// service key. A missing or revoked service key surfaces as Unhealthy instead
/// of a runtime 401 the first time a user touches auth-admin functionality.
///
/// Exercises the same <see cref="IUserInfoService"/> production uses for
/// fetching user info, so the health check covers the exact client wiring.
/// </summary>
internal sealed class SupabaseAuthHealthCheck(IUserInfoService userInfoService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await userInfoService.PingAsync(ct);
            return HealthCheckResult.Healthy("Supabase Auth admin API reachable with the configured service key.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Supabase Auth admin API rejected the request. Verify Supabase:ProjectUrl and Supabase:ServiceKey.",
                ex);
        }
    }
}
