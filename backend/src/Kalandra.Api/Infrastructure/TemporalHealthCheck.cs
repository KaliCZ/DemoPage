using Microsoft.Extensions.Diagnostics.HealthChecks;
using Temporalio.Client;

namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Verifies the Temporal server is reachable over gRPC. Blog-comment and
/// job-offer submissions run through Temporal workflows, so an unreachable
/// server breaks those writes while every read path keeps working — hence
/// Degraded, not Unhealthy: readiness is partial, not down.
/// </summary>
internal sealed class TemporalHealthCheck(ITemporalClient temporalClient) : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        // Temporal's client honors cancellation — unlike the Supabase probes, the timeout can
        // cancel the RPC itself, leaving no abandoned task behind.
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts.CancelAfter(ProbeTimeout);
        try
        {
            var serving = await temporalClient.Connection
                .CheckHealthAsync(options: new RpcOptions { CancellationToken = probeCts.Token });
            return serving
                ? HealthCheckResult.Healthy("Temporal server reachable and serving.")
                : HealthCheckResult.Degraded("Temporal server reachable but not serving.");
        }
        catch (Exception ex) when (probeCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded($"Temporal server did not respond within {ProbeTimeout.TotalSeconds:0}s.", ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Temporal server unreachable.", ex);
        }
    }
}
