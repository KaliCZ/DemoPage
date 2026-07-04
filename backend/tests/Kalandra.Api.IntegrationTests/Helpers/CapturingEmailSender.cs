using System.Collections.Concurrent;
using Kalandra.Infrastructure.Email;

namespace Kalandra.Api.IntegrationTests.Helpers;

public class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => sent;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
