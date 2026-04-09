namespace Kalandra.Infrastructure.Auth;

public enum ChangePasswordError
{
    AlreadyLinked,
    Unknown,
}

public interface ISupabaseAdminService
{
    /// <summary>
    /// Sets the password for the given user via the Supabase Admin API.
    /// Also links an email/password identity if the account was created via
    /// OAuth and does not yet have one.
    /// Returns null on success, or a typed error on failure.
    /// </summary>
    Task<ChangePasswordError?> ChangePasswordAsync(
        CurrentUser user,
        string password,
        CancellationToken ct);
}
