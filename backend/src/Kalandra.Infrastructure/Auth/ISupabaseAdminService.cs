namespace Kalandra.Infrastructure.Auth;

public enum ChangePasswordErrorCode
{
    AlreadyLinked,
    Unknown,
}

/// <summary>Message is Supabase's raw error text, kept so the caller can log it.</summary>
public record ChangePasswordError(ChangePasswordErrorCode Code, string Message);

public interface ISupabaseAdminService
{
    /// <summary>Also links an email/password identity to an OAuth-only account; returns null on success.</summary>
    Task<ChangePasswordError?> ChangePasswordAsync(
        CurrentUser user,
        string password,
        CancellationToken ct);
}
