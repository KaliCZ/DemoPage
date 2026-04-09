using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using Kalandra.Infrastructure.Auth;

namespace Kalandra.Api.Infrastructure.Auth;

public class HttpContextCurrentUserAccessor(
    IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    private static readonly AsyncLocal<CurrentUser?> CachedUser = new();
    public CurrentUser? User => CachedUser.Value ??= BuildCurrentUser();
    public CurrentUser RequiredUser => User ?? throw new InvalidOperationException("No authenticated user on the current request.");

    private CurrentUser? BuildCurrentUser()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var userIdStr = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var emailStr = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue(JwtRegisteredClaimNames.Email);

        if (!Guid.TryParse(userIdStr, out var userId) || !MailAddress.TryCreate(emailStr, out var email))
            return null;

        var userMetadata = principal.FindFirstValue("user_metadata");
        var (fullName, avatarUrl) = ExtractUserMetadata(userMetadata, email);

        return new CurrentUser(
            Id: userId,
            Email: email,
            FullName: fullName,
            Roles: ExtractRoles(principal),
            AvatarUrl: avatarUrl
        );
    }

    /// <summary>
    /// Translates ASP.NET role claims into the Role enum. Claim values are
    /// canonicalized to enum names by Auth.ExtractRolesFromAppMetadata, so a
    /// strict (case-sensitive) parse is enough here.
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
    private static (string FullName, Uri? AvatarUrl) ExtractUserMetadata(string? userMetadata, MailAddress email)
    {
        if (string.IsNullOrEmpty(userMetadata))
            return (email.User, null);

        using var doc = JsonDocument.Parse(userMetadata);

        string? fullName = null;
        if (doc.RootElement.TryGetProperty("full_name", out var fullNameProp) &&
            fullNameProp.ValueKind == JsonValueKind.String)
        {
            var name = fullNameProp.GetString();
            if (!string.IsNullOrEmpty(name))
                fullName = name;
        }

        Uri? avatarUrl = null;
        if (doc.RootElement.TryGetProperty("avatar_url", out var avatarProp) &&
            avatarProp.ValueKind == JsonValueKind.String)
        {
            var url = avatarProp.GetString();
            if (!string.IsNullOrEmpty(url))
                Uri.TryCreate(url, UriKind.Absolute, out avatarUrl);
        }

        return (fullName ?? email.User, avatarUrl);
    }
}
