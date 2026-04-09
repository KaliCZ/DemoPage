using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;

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

        var userIdStr =
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var emailStr =
            principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Email);

        if (userIdStr is null || emailStr is null)
            return null;

        if (!Guid.TryParse(userIdStr, out var userId))
            return null;

        if (!MailAddress.TryCreate(emailStr, out var email))
            return null;

        return new CurrentUser(
            Id: userId,
            Email: email,
            DisplayName: principal.FindFirstValue("display_name") ?? email.User,
            Roles: principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToImmutableArray()
        );
    }
}
