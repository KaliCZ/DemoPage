using Kalandra.Blog.Commands;
using Kalandra.Blog.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.Blog;

public static class ServiceRegistration
{
    public static IServiceCollection AddBlogDomain(this IServiceCollection services)
    {
        // Command handlers
        services.AddScoped<ToggleBlogReactionHandler>();
        services.AddScoped<PostBlogCommentHandler>();
        services.AddScoped<DeleteBlogCommentHandler>();

        // Query handlers
        services.AddScoped<GetBlogReactionsHandler>();
        services.AddScoped<GetBlogCommentsHandler>();

        return services;
    }
}
