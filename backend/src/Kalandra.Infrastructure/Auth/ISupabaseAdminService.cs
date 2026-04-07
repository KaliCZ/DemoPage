namespace Kalandra.Infrastructure.Auth;

public interface ISupabaseAdminService
{
    /// <summary>
    /// Updates a Supabase user via the Admin API.
    /// Used to link email/password identity to an existing OAuth user.
    /// </summary>
    Task<SupabaseAdminResult> UpdateUserAsync(
        string userId,
        object updatePayload,
        CancellationToken ct);
}

public record SupabaseAdminResult(bool Success, string? Error = null);
