using Kalandra.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;

namespace Kalandra.Hosting.Auth;

public class HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    private static readonly AsyncLocal<CurrentUser?> CachedUser = new();
    public CurrentUser? User => CachedUser.Value ??= CurrentUserFactory.FromClaimsPrincipal(httpContextAccessor.HttpContext?.User);
    public CurrentUser RequiredUser => User ?? throw new InvalidOperationException("No authenticated user on the current request.");
}
