namespace Kalandra.Infrastructure.Auth;

public interface ISupabaseAdminService
{
    /// <summary>
    /// Sets the password for the given user via the Supabase Admin API.
    /// Also links an email/password identity if the account was created via
    /// OAuth and does not yet have one — the admin update carries the user's
    /// email and email_confirm alongside the password, which Supabase treats
    /// as identity linking when no password identity exists.
    /// </summary>
    Task<SupabaseAdminResult> ChangePasswordAsync(
        CurrentUser user,
        string password,
        CancellationToken ct);
}

public record SupabaseAdminResult(bool Success, string? Error = null);
