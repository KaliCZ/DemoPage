using System.Collections.Immutable;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using StrongTypes;

namespace Kalandra.Infrastructure.Auth;

/// <summary>
/// Builds a <see cref="CurrentUser"/> from the validated JWT claims Supabase issues. Shared by every
/// host's request-scoped accessor so the API and the MCP server read the same identity from a token.
/// </summary>
public static class CurrentUserFactory
{
    public static CurrentUser? FromClaimsPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        // "sub"/"email" are the raw JWT names; ClaimTypes.* are the mapped forms — accept either.
        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
        var emailStr = principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst("email")?.Value;

        if (!Guid.TryParse(userIdStr, out var userId) || !MailAddress.TryCreate(emailStr, out var email))
            return null;

        var (fullName, avatarUrl) = ExtractUserMetadata(principal.FindFirst("user_metadata")?.Value, email);

        return new CurrentUser(
            Id: userId,
            Email: email,
            FullName: fullName,
            Roles: ExtractRoles(principal),
            AvatarUrl: avatarUrl);
    }

    /// <summary>
    /// Role claim values are canonicalized to enum names when the token is validated,
    /// so a strict (case-sensitive) parse is enough here.
    /// </summary>
    private static ImmutableArray<UserRole> ExtractRoles(ClaimsPrincipal principal)
    {
        var builder = ImmutableArray.CreateBuilder<UserRole>();
        foreach (var claim in principal.FindAll(ClaimTypes.Role))
        {
            if (Enum.TryParse<UserRole>(claim.Value, out var role))
                builder.Add(role);
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses user_metadata looking for "full_name" and "avatar_url",
    /// falling back to the email's local part for the name.
    /// </summary>
    private static (NonEmptyString FullName, Uri? AvatarUrl) ExtractUserMetadata(string? userMetadata, MailAddress email)
    {
        // MailAddress guarantees a non-empty local part on a successful construction.
        var fallbackName = NonEmptyString.Create(email.User);

        if (string.IsNullOrEmpty(userMetadata))
            return (fallbackName, null);

        using var doc = JsonDocument.Parse(userMetadata);

        NonEmptyString? fullName = null;
        if (doc.RootElement.TryGetProperty("full_name", out var fullNameProp) &&
            fullNameProp.ValueKind == JsonValueKind.String)
        {
            fullName = fullNameProp.GetString().AsNonEmpty();
        }

        Uri? avatarUrl = null;
        if (doc.RootElement.TryGetProperty("avatar_url", out var avatarProp) &&
            avatarProp.ValueKind == JsonValueKind.String)
        {
            var url = avatarProp.GetString();
            if (!string.IsNullOrEmpty(url))
                Uri.TryCreate(url, UriKind.Absolute, out avatarUrl);
        }

        return (fullName ?? fallbackName, avatarUrl);
    }
}
