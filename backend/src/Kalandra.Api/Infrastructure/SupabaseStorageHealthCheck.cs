using Kalandra.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Verifies that the Supabase Storage bucket used for job-offer attachments is
/// reachable with the configured service key. Catches misconfigured project
/// URLs, revoked service keys, and missing buckets at /health rather than on
/// the first upload attempt.
///
/// Exercises the same <see cref="IStorageService"/> the production upload path
/// uses, so the health check is guaranteed to cover the exact client wiring.
/// </summary>
internal sealed class SupabaseStorageHealthCheck(IStorageService storageService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await storageService.PingAsync(ct);
            return HealthCheckResult.Healthy("Supabase Storage reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Supabase Storage unreachable. Verify Supabase:ProjectUrl, Supabase:ServiceKey, and that the bucket exists.",
                ex);
        }
    }
}
