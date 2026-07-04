using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.Blog;

/// <summary>Where new-comment notifications for the blog author land.</summary>
public record BlogNotificationsConfig(Email AuthorEmail)
{
    public static BlogNotificationsConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var config = new BlogNotificationsConfig(
            AuthorEmail: Email.Create(configuration["Blog:AuthorNotificationEmail"]));

        services.AddSingleton(config);

        return config;
    }
}
