using System.Text.Json;

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

    /// <summary>
    /// Gets a Supabase user by ID via the Admin API.
    /// </summary>
    Task<JsonElement?> GetUserAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Deletes an identity from a Supabase user via the Admin API.
    /// </summary>
    Task<SupabaseAdminResult> DeleteIdentityAsync(
        string identityId,
        CancellationToken ct);
}

public record SupabaseAdminResult(bool Success, string? Error = null);
