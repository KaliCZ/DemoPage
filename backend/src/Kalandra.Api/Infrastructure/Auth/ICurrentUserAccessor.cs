namespace Kalandra.Api.Infrastructure.Auth;

public interface ICurrentUserAccessor
{
    string RequireUserId();

    string? GetEmail();

    string GetDisplayName();

    Task<bool> IsAdminAsync();
}
