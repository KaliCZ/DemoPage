using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kalandra.Blog;

/// <summary>Where new-comment notifications for the blog author land.</summary>
public record BlogNotificationsConfig(MailAddress AuthorEmail)
{
    public static BlogNotificationsConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var authorEmail = configuration["Blog:AuthorNotificationEmail"]
            ?? throw new InvalidOperationException(
                "Blog:AuthorNotificationEmail is not configured — comment notifications have no author recipient.");

        var config = new BlogNotificationsConfig(AuthorEmail: new MailAddress(authorEmail));

        // .local is the dev placeholder domain; in production it would drop every notification.
        if (!environment.IsDevelopment() && config.AuthorEmail.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Blog:AuthorNotificationEmail is still a .local address — a production deploy must set a real recipient.");

        services.AddSingleton(config);

        return config;
    }
}
