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

    // MailAddress guarantees a non-empty Address on a successfully constructed
    // value, so this conversion is always valid. Exposing it saves domain code
    // from calling NonEmptyString.Create on every emitted event.
    public NonEmptyString EmailAddress => NonEmptyString.Create(Email.Address);
}
