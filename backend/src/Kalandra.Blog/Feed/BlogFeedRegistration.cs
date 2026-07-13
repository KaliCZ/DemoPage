using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kalandra.Blog.Feed;

public static class BlogFeedRegistration
{
    public static IServiceCollection AddBlogFeed(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        BlogFeedConfig.AddSingleton(services, configuration, environment);
        services.AddHttpClient<BlogFeedClient>();
        return services;
    }
}
