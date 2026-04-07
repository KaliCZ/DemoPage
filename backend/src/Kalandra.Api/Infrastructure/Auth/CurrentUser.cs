using System.Collections.Immutable;

namespace Kalandra.Api.Infrastructure.Auth;

public record CurrentUser(
    string Id,
    string Email,
    string DisplayName,
    ImmutableArray<string> Roles,
    string? AvatarUrl = null)
{
    public bool IsAdmin => Roles.Contains("admin");
}
