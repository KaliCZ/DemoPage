using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;

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

        return new CurrentUser(
            Id: userId,
            Email: email,
            FullName: ExtractFullName(principal.FindFirstValue("user_metadata"), email),
            Roles: ExtractRoles(principal)
        );
    }

    /// <summary>
    /// Translates ASP.NET role claims (string-valued, added in
    /// SupabaseJwtSetup) into the Role enum. Unknown role strings are
    /// silently dropped — the role claim stays on the principal either way
    /// for [Authorize(Policy="...")] to use, this is just the strongly-typed
    /// projection we expose to application code.
    /// </summary>
    private static ImmutableArray<Role> ExtractRoles(ClaimsPrincipal principal)
    {
        var builder = ImmutableArray.CreateBuilder<Role>();
        foreach (var claim in principal.FindAll(ClaimTypes.Role))
        {
            if (Enum.TryParse<Role>(claim.Value, ignoreCase: true, out var role))
                builder.Add(role);
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Parses user_metadata looking for "full_name", falling back to the
    /// email's local part.
    /// </summary>
    private static string ExtractFullName(string? userMetadata, MailAddress email)
    {
        if (string.IsNullOrEmpty(userMetadata))
            return email.User;

        using var doc = JsonDocument.Parse(userMetadata);

        if (doc.RootElement.TryGetProperty("full_name", out var fullName) &&
            fullName.ValueKind == JsonValueKind.String)
        {
            var name = fullName.GetString();
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return email.User;
    }
}
