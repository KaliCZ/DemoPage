using System.Net.Mail;
using StrongTypes;

namespace Kalandra.Infrastructure.StrongTypesExtensions;

// Candidate to move into the Kalicz.StrongTypes package once it grows a BCL-types
// extension surface. Kept as an extension (rather than a property on a wrapper
// type) so existing MailAddress consumers can opt-in without touching their type.
public static class MailAddressExtensions
{
    /// <summary>
    /// Converts a <see cref="MailAddress"/> to a <see cref="NonEmptyString"/>.
    /// A successfully-constructed <see cref="MailAddress"/> always has a non-empty
    /// <see cref="MailAddress.Address"/>, so this conversion never throws.
    /// </summary>
    public static NonEmptyString ToNonEmpty(this MailAddress address) =>
        NonEmptyString.Create(address.Address);
}
