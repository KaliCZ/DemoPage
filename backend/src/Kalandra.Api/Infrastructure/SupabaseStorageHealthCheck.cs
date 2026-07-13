using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Verifies that the Supabase Storage bucket used for job-offer attachments is
/// reachable with the configured service key. Catches misconfigured project
/// URLs, revoked service keys, and missing buckets at /health rather than on
/// the first upload attempt. Failures report Degraded, not Unhealthy: uploads
/// break while the rest of the API keeps serving, so readiness is partial, not down.
///
/// Exercises the same <see cref="IStorageService"/> the production upload path
/// uses, so the health check is guaranteed to cover the exact client wiring.
/// </summary>
internal sealed class SupabaseStorageHealthCheck(IStorageService storageService) : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            // Supabase's storage client ignores cancellation, so the timeout is enforced here, not via the check registration.
            await storageService.PingAsync(ct).WaitObservedAsync(ProbeTimeout, ct);
            return HealthCheckResult.Healthy("Supabase Storage reachable.");
        }
        catch (TimeoutException ex)
        {
            return HealthCheckResult.Degraded($"Supabase Storage did not respond within {ProbeTimeout.TotalSeconds:0}s.", ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded(
                "Supabase Storage unreachable. Verify Supabase:ProjectUrl, Supabase:ServiceKey, and that the bucket exists.",
                ex);
        }
    }
}
