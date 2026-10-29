using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Reports Degraded rather than Unhealthy because a storage failure breaks only uploads, not the rest of the API.
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
