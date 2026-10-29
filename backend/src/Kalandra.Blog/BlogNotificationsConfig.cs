using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kalandra.Blog;

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

        if (!environment.IsDevelopment() && config.AuthorEmail.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Blog:AuthorNotificationEmail is still a .local address — a production deploy must set a real recipient.");

        services.AddSingleton(config);

        return config;
    }
}
