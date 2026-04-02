using System.Collections.Immutable;

namespace Kalandra.Api.Infrastructure.Auth;

public record CurrentUser(
    string Id,
    string Email,
    string DisplayName,
    ImmutableArray<string> Roles)
{
    public bool IsAdmin => Roles.Contains("admin");
}
