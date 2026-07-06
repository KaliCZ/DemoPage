using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.Blog;

/// <summary>Where new-comment notifications for the blog author land.</summary>
public record BlogNotificationsConfig(MailAddress AuthorEmail)
{
    public static BlogNotificationsConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var authorEmail = configuration["Blog:AuthorNotificationEmail"]
            ?? throw new InvalidOperationException(
                "Blog:AuthorNotificationEmail is not configured — comment notifications have no author recipient.");

        var config = new BlogNotificationsConfig(AuthorEmail: new MailAddress(authorEmail));

        services.AddSingleton(config);

        return config;
    }
}
