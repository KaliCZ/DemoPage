namespace Kalandra.Api;

/// <summary>
/// Standard error response carrying a strongly-typed error code.
/// The API's global JsonStringEnumConverter serializes <typeparamref name="TError"/>
/// as its name string, e.g. <c>{ "error": "PasswordTooShort" }</c>.
/// </summary>
public record ErrorResponse<TError>(TError Error) where TError : struct, Enum;
