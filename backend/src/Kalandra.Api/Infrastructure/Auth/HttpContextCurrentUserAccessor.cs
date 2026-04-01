using System.Collections.Immutable;
using System.Security.Claims;

namespace Kalandra.Api.Infrastructure.Auth;

public class HttpContextCurrentUserAccessor(
    IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    private static readonly AsyncLocal<CurrentUser?> CachedUser = new();

    public CurrentUser CurrentUser => CachedUser.Value ??= BuildCurrentUser();

    private CurrentUser BuildCurrentUser()
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("HTTP context is not available.");

        var userId = principal.GetUserId()
            ?? throw new InvalidOperationException("Authenticated user ID is not available.");

        var email = principal.GetEmail()
            ?? throw new InvalidOperationException("Authenticated user email is not available.");

        var displayName = principal.FindFirstValue("display_name")
            ?? email.Split('@')[0];

        var roles = principal.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToImmutableArray();

        return new CurrentUser(
            Id: userId,
            Email: email,
            DisplayName: displayName,
            Roles: roles);
    }
}
