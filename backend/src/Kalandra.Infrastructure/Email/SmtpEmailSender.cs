using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Kalandra.Infrastructure.Email;

public class SmtpEmailSender(EmailConfig config, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(config.FromName.Value, config.FromEmail.Value.Address));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To.Value.Address));
        mimeMessage.Subject = message.Subject;
        mimeMessage.Body = new TextPart("plain") { Text = message.TextBody };

        // Auto picks STARTTLS on 587 and stays plaintext against the local dev
        // mail catcher, which offers no TLS.
        using var client = new SmtpClient();
        await client.ConnectAsync(config.Host, config.Port, SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(config.Username))
            await client.AuthenticateAsync(config.Username, config.Password ?? "", ct);
        await client.SendAsync(mimeMessage, ct);
        await client.DisconnectAsync(quit: true, ct);

        logger.LogInformation("Sent email \"{Subject}\" to {Recipient}", message.Subject.Value, message.To.Value.Address);
    }
}
