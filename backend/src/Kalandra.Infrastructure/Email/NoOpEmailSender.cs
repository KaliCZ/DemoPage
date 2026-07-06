using Microsoft.Extensions.Logging;

namespace Kalandra.Infrastructure.Email;

/// <summary>Development-only stand-in when no Email config is present; production refuses to start without one.</summary>
public class NoOpEmailSender(ILogger<NoOpEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogWarning("Email sending is not configured — dropping \"{Subject}\" to {Recipient}",
            message.Subject.Value, message.To.Address);
        return Task.CompletedTask;
    }
}
