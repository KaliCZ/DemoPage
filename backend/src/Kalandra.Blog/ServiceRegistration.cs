using Kalandra.Blog.Commands;
using Kalandra.Blog.Queries;
using Kalandra.Blog.Stats;
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
        services.AddScoped<PostBlogCommentHandler>();
        services.AddScoped<DeleteBlogCommentHandler>();
        services.AddScoped<RecordBlogPostViewHandler>();
        services.AddScoped<LinkVisitorHandler>();

        // Query handlers
        services.AddScoped<GetBlogReactionsHandler>();
        services.AddScoped<GetBlogCommentsHandler>();
        services.AddScoped<GetBlogPostStatsHandler>();

        // Stats snapshot: the store owns one connection pool (singleton); the refresher reads through a session (scoped).
        // The NpgsqlDataSource the store depends on is registered by the API's AddBlogStatsSnapshot.
        services.AddSingleton<BlogStatsSnapshotStore>();
        services.AddScoped<BlogStatsSnapshotRefresher>();

        return services;
    }
}
