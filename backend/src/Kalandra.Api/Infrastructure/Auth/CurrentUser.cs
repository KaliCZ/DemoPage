using System.Collections.Immutable;
using System.Net.Mail;

namespace Kalandra.Api.Infrastructure.Auth;

public enum Role
{
    Admin,
}

public record CurrentUser(
    Guid Id,
    MailAddress Email,
    string FullName,
    ImmutableArray<Role> Roles)
{
    public bool IsAdmin => Roles.Contains(Role.Admin);
}
