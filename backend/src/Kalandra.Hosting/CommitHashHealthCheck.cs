using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalandra.Hosting;

public sealed class CommitHashHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        return Task.FromResult(new HealthCheckResult(HealthStatus.Healthy, data: new Dictionary<string, object>
        {
            { "commit", AppVersion.CommitHash }
        }));
    }
}
