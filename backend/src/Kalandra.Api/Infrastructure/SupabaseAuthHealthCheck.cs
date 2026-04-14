using Microsoft.Extensions.Diagnostics.HealthChecks;
using Supabase;

namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Verifies that the Supabase Auth admin API is reachable with the configured
/// service key by issuing a lightweight ListUsers call (per-page=1). A missing
/// or revoked service key surfaces as Unhealthy instead of a runtime 401 the
/// first time a user touches auth-admin functionality.
/// </summary>
internal sealed class SupabaseAuthHealthCheck(
    Client supabase,
    Kalandra.Infrastructure.Configuration.SupabaseConfig supabaseConfig) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var adminClient = supabase.AdminAuth(supabaseConfig.ServiceKey.Value);
            // perPage=1 keeps the round-trip minimal; we only need to confirm the key is accepted.
            await adminClient.ListUsers(filter: null, sortBy: null, sortOrder: Supabase.Gotrue.Constants.SortOrder.Descending, page: null, perPage: 1);
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
