namespace Kalandra.Api.Infrastructure.Auth;

public record CurrentUser(
    string Id,
    string Email,
    string DisplayName,
    bool IsAdmin);
