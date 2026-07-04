using StrongTypes;

namespace Kalandra.Infrastructure.Email;

public record EmailMessage(
    StrongTypes.Email To,
    NonEmptyString Subject,
    NonEmptyString TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
