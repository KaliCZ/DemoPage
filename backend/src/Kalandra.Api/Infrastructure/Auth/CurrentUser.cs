using System.Collections.Immutable;
using System.Net.Mail;

namespace Kalandra.Api.Infrastructure.Auth;

public record CurrentUser(
    Guid Id,
    MailAddress Email,
    Lazy<string> LazyDisplayName,
    ImmutableArray<string> Roles)
{
    /// <summary>
    /// Human-readable name. Parsed lazily from the JWT's user_metadata claim
    /// on first access — endpoints that never read this don't pay for it.
    /// </summary>
    public string DisplayName => LazyDisplayName.Value;

    public bool IsAdmin => Roles.Contains("admin");
}
