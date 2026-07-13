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
        services.AddSingleton<BlogCommentCountCache>();

        // Command handlers
        services.AddScoped<ToggleBlogReactionHandler>();
        services.AddScoped<PostBlogCommentHandler>();
        services.AddScoped<DeleteBlogCommentHandler>();
        services.AddScoped<RecordBlogPostViewHandler>();
        services.AddScoped<LinkVisitorHandler>();

        // Query handlers
        services.AddScoped<GetBlogReactionsHandler>();
        services.AddScoped<GetBlogCommentsHandler>();
        services.AddScoped<GetBlogPostStatsHandler>();
        services.AddScoped<GetViewerBlogViewsHandler>();
        services.AddScoped<ListMyBlogCommentsHandler>();

        return services;
    }
}
