using Microsoft.Extensions.Diagnostics.HealthChecks;
using Temporalio.Client;

namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Verifies the Temporal server is reachable over gRPC. Blog-comment and
/// job-offer submissions run through Temporal workflows, so an unreachable
/// server breaks those writes while every read path keeps working.
/// </summary>
internal sealed class TemporalHealthCheck(ITemporalClient temporalClient) : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var serving = await temporalClient.Connection
                .CheckHealthAsync(options: new RpcOptions { CancellationToken = ct })
                .WaitAsync(ProbeTimeout, ct);
            return serving
                ? HealthCheckResult.Healthy("Temporal server reachable and serving.")
                : new HealthCheckResult(context.Registration.FailureStatus, "Temporal server reachable but not serving.");
        }
        catch (TimeoutException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus,
                $"Temporal server did not respond within {ProbeTimeout.TotalSeconds:0}s.", ex);
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Temporal server unreachable.", ex);
        }
    }
}
