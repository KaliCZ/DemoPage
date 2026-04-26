using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace Kalandra.Api.StrongTypesExtensions;

/// <summary>
/// Email-format validation that also accepts <see cref="NonEmptyString"/> and
/// <see cref="NonEmptyString?"/>. The BCL <see cref="EmailAddressAttribute"/>
/// is sealed and rejects anything that isn't a <c>string</c>, so use this
/// attribute on <c>NonEmptyString</c>-typed DTO fields.
/// </summary>
// Candidate to move into a future Kalicz.StrongTypes.DataAnnotations package.
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class EmailFormatAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        var raw = value switch
        {
            null => null,
            string s => s,
            NonEmptyString nes => nes.Value,
            _ => null,
        };

        if (raw is null)
            return value is null;

        return MailAddress.TryCreate(raw, out _);
    }

    public override string FormatErrorMessage(string name) =>
        $"The {name} field is not a valid e-mail address.";
}
