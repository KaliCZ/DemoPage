using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrongTypes;

namespace Kalandra.Infrastructure.Email;

public record SmtpCredentials(NonEmptyString Username, NonEmptyString Password);

public record EmailConfig(
    NonEmptyString Host,
    int Port,
    SmtpCredentials? Credentials,
    MailAddress FromEmail,
    NonEmptyString FromName)
{
    public static EmailConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection("Email");

        var username = NonEmptyString.TryCreate(section["Username"]);
        var password = NonEmptyString.TryCreate(section["Password"]);
        var credentials = username is { } u && password is { } p ? new SmtpCredentials(u, p) : null;

        var host = NonEmptyString.Create(section["Host"]);
        var isLoopback = host.Value is "localhost" or "127.0.0.1";

        if (!environment.IsDevelopment() && isLoopback)
            throw new InvalidOperationException(
                "Email:Host still points at the local mail catcher — a production deploy must set a real SMTP relay.");

        // Only the loopback mail catcher accepts unauthenticated mail; fail fast rather than have a real relay silently drop it.
        if (credentials is null && !isLoopback)
            throw new InvalidOperationException(
                "Email:Username and Email:Password are required unless Email:Host is the local mail catcher (localhost).");

        var config = new EmailConfig(
            Host: host,
            Port: int.Parse(section["Port"] ?? "587"),
            Credentials: credentials,
            FromEmail: new MailAddress(section["FromEmail"]
                ?? throw new InvalidOperationException("Email:FromEmail is not configured.")),
            FromName: NonEmptyString.Create(section["FromName"]));

        services.AddSingleton(config);

        return config;
    }
}
