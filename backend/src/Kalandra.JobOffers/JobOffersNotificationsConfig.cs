using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kalandra.JobOffers;

/// <summary>Where new-offer and comment notifications for the site owner land.</summary>
public record JobOffersNotificationsConfig(MailAddress OwnerEmail)
{
    public static JobOffersNotificationsConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var ownerEmail = configuration["JobOffers:OwnerNotificationEmail"]
            ?? throw new InvalidOperationException(
                "JobOffers:OwnerNotificationEmail is not configured — job offer notifications have no owner recipient.");

        var config = new JobOffersNotificationsConfig(OwnerEmail: new MailAddress(ownerEmail));

        // .local is the dev placeholder domain; in production it would drop every notification.
        if (!environment.IsDevelopment() && config.OwnerEmail.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "JobOffers:OwnerNotificationEmail is still a .local address — a production deploy must set a real recipient.");

        services.AddSingleton(config);

        return config;
    }
}
