using Microsoft.AspNetCore.Authorization;

namespace Kalandra.Api.Infrastructure.Auth;

public class HttpContextCurrentUserAccessor(
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService) : ICurrentUserAccessor
{
    public string RequireUserId()
    {
        var userId = httpContextAccessor.HttpContext?.User.GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Authenticated user ID is not available.");
        }

        return userId;
    }

    public string? GetEmail() => httpContextAccessor.HttpContext?.User.GetEmail();

    public string GetDisplayName()
    {
        var user = httpContextAccessor.HttpContext?.User;
        var email = user?.GetEmail() ?? "";

        return user?.FindFirst("user_metadata.full_name")?.Value
            ?? user?.FindFirst("name")?.Value
            ?? email.Split('@')[0];
    }

    public async Task<bool> IsAdminAsync()
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is not available.");

        var result = await authorizationService.AuthorizeAsync(httpContext.User, "Admin");
        return result.Succeeded;
    }
}
