using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalandra.Api.Infrastructure;

internal sealed class CommitHashHealthCheck : IHealthCheck
{
    private static readonly string CommitHash = GetCommitHash();

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        return Task.FromResult(new HealthCheckResult(HealthStatus.Healthy, data: new Dictionary<string, object>
        {
            { "commit", CommitHash }
        }));
    }

    private static string GetCommitHash()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return version is { } v && v.Contains('+')
            ? v[(v.IndexOf('+') + 1)..]
            : version ?? "unknown";
    }
}
