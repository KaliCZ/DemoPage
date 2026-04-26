using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.StrongTypesExtensions;

/// <summary>
/// Drop-in replacement for <see cref="MaxLengthAttribute"/> that also accepts
/// <see cref="NonEmptyString"/> and <see cref="NonEmptyString?"/>. The BCL
/// <see cref="MaxLengthAttribute"/> throws <c>InvalidCastException</c> when the
/// validated property is a strong-typed wrapper, so use this attribute on
/// <c>NonEmptyString</c>-typed DTO fields.
/// </summary>
// Candidate to move into a future Kalicz.StrongTypes.DataAnnotations package.
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class StringMaxLengthAttribute(int maximumLength) : ValidationAttribute
{
    public int MaximumLength { get; } = maximumLength;

    public override bool IsValid(object? value) => value switch
    {
        null => true,
        string s => s.Length <= MaximumLength,
        NonEmptyString nes => nes.Value.Length <= MaximumLength,
        _ => false,
    };

    public override string FormatErrorMessage(string name) =>
        $"The field {name} must be a string with a maximum length of {MaximumLength}.";
}
