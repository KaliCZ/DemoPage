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
        var principal = httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("HTTP context is not available.");
        var email = principal.GetEmail() ?? throw new InvalidOperationException("Authenticated user email is not available.");

        return new CurrentUser(
            Id: principal.GetUserId() ?? throw new InvalidOperationException("Authenticated user ID is not available."),
            Email: email,
            DisplayName: principal.FindFirstValue("display_name") ?? email.Split('@')[0],
            Roles: principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToImmutableArray()
        );
    }
}
