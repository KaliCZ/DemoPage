using System.Net.Mail;
using StrongTypes;

namespace Kalandra.Infrastructure.Email;

public record EmailMessage(
    MailAddress To,
    NonEmptyString Subject,
    NonEmptyString TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
