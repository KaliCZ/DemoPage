using System.Collections.Immutable;
using System.Net.Mail;
using StrongTypes;

namespace Kalandra.Infrastructure.Auth;

public enum UserRole
{
    Admin,
}

public record CurrentUser(
    Guid Id,
    MailAddress Email,
    NonEmptyString FullName,
    ImmutableArray<UserRole> Roles,
    Uri? AvatarUrl = null)
{
    public bool IsAdmin => Roles.Contains(UserRole.Admin);
}
