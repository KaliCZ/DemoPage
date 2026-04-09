using System.Collections.Immutable;
using System.Net.Mail;

namespace Kalandra.Infrastructure.Auth;

public enum UserRole
{
    Admin,
}

public record CurrentUser(
    Guid Id,
    MailAddress Email,
    string FullName,
    ImmutableArray<UserRole> Roles)
{
    public bool IsAdmin => Roles.Contains(UserRole.Admin);
}
