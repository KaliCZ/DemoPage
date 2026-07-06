using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Email;

/// <summary>
/// SMTP login is genuinely optional at the protocol level: the local mail catcher
/// accepts unauthenticated mail, a production relay requires a login. Coupled so a
/// username can never exist without its password.
/// </summary>
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
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Email");

        var username = NonEmptyString.TryCreate(section["Username"]);
        var password = NonEmptyString.TryCreate(section["Password"]);
        var credentials = username is { } u && password is { } p ? new SmtpCredentials(u, p) : null;

        var config = new EmailConfig(
            Host: NonEmptyString.Create(section["Host"]),
            Port: int.Parse(section["Port"] ?? "587"),
            Credentials: credentials,
            FromEmail: new MailAddress(section["FromEmail"]
                ?? throw new InvalidOperationException("Email:FromEmail is not configured.")),
            FromName: NonEmptyString.Create(section["FromName"]));

        services.AddSingleton(config);

        return config;
    }
}
