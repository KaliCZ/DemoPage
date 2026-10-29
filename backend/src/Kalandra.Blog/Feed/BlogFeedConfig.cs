using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrongTypes;

namespace Kalandra.Blog.Feed;

public record BlogFeedConfig(Uri RssUrl)
{
    public static BlogFeedConfig AddSingleton(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var rssUrl = NonEmptyString.Create(configuration.GetSection("BlogFeed")["RssUrl"]).Value;

        if (!environment.IsDevelopment()
            && (rssUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) || rssUrl.Contains("127.0.0.1")))
            throw new InvalidOperationException(
                "BlogFeed:RssUrl still points at localhost — a production deploy must set the real feed URL.");

        var config = new BlogFeedConfig(new Uri(rssUrl));
        services.AddSingleton(config);
        return config;
    }
}
