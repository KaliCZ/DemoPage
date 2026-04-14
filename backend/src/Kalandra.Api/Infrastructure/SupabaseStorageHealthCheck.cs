using Microsoft.Extensions.Diagnostics.HealthChecks;
using Kalandra.Infrastructure.Storage;
using Supabase.Storage;

namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Verifies that the Supabase Storage bucket used for job-offer attachments is
/// reachable with the configured service key. Catches misconfigured project
/// URLs, revoked service keys, and missing buckets at /health rather than on
/// the first upload attempt.
/// </summary>
internal sealed class SupabaseStorageHealthCheck(Supabase.Storage.Client storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var searchOptions = new SearchOptions { Limit = 1 };
            await storage.From(SupabaseStorageService.BucketName).List(path: "", options: searchOptions);
            return HealthCheckResult.Healthy($"Supabase Storage bucket '{SupabaseStorageService.BucketName}' reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Supabase Storage bucket '{SupabaseStorageService.BucketName}' unreachable. Verify Supabase:ProjectUrl, Supabase:ServiceKey, and that the bucket exists.",
                ex);
        }
    }
}
