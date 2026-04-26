using System.Collections.Immutable;
using System.Net.Mail;
using Kalandra.Infrastructure.StrongTypesExtensions;
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

    // MailAddress.Address is non-empty by construction; the
    // MailAddressExtensions helper makes that conversion explicit so domain
    // code doesn't need to call NonEmptyString.Create on every emitted event.
    public NonEmptyString EmailAddress => Email.ToNonEmpty();
}
