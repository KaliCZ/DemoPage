using Kalandra.Blog.Commands;
using Kalandra.Blog.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.Blog;

public static class ServiceRegistration
{
    public static IServiceCollection AddBlogDomain(this IServiceCollection services)
    {
        // Which slugs are real posts (drives the reactions/comments slug gate).
        services.AddSingleton<IBlogPostCatalog, BlogPostCatalog>();

        // Command handlers
        services.AddScoped<ToggleBlogReactionHandler>();
        services.AddScoped<StoreBlogCommentHandler>();
        services.AddScoped<DeleteBlogCommentHandler>();

        // Query handlers
        services.AddScoped<GetBlogReactionsHandler>();
        services.AddScoped<GetBlogCommentsHandler>();

        return services;
    }
}
