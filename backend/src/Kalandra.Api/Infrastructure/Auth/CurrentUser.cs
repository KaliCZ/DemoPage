using System.Collections.Immutable;
using System.Net.Mail;

namespace Kalandra.Api.Infrastructure.Auth;

public record CurrentUser(
    Guid Id,
    MailAddress Email,
    string DisplayName,
    ImmutableArray<string> Roles)
{
    public bool IsAdmin => Roles.Contains("admin");
}
