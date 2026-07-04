using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Email;

public record EmailConfig(
    NonEmptyString Host,
    int Port,
    string? Username,
    string? Password,
    StrongTypes.Email FromEmail,
    NonEmptyString FromName)
{
    public static EmailConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Email");

        var config = new EmailConfig(
            Host: NonEmptyString.Create(section["Host"]),
            Port: int.Parse(section["Port"] ?? "587"),
            Username: section["Username"],
            Password: section["Password"],
            FromEmail: StrongTypes.Email.Create(section["FromEmail"]),
            FromName: NonEmptyString.Create(section["FromName"]));

        services.AddSingleton(config);

        return config;
    }
}
