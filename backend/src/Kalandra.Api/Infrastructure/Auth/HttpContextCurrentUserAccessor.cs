using System.Collections.Immutable;
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

        var userIdStr = principal.GetUserId();
        var email = principal.GetEmail();
        if (userIdStr is null || email is null)
            return null;

        if (!Guid.TryParse(userIdStr, out var userId))
            return null;

        return new CurrentUser(
            Id: userId,
            Email: email,
            DisplayName: principal.FindFirstValue("display_name") ?? email.Split('@')[0],
            Roles: principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToImmutableArray()
        );
    }
}
