using Kalandra.Blog.Commands;
using Kalandra.Blog.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.Blog;

public static class ServiceRegistration
{
    public static IServiceCollection AddBlogDomain(this IServiceCollection services)
    {
        services.AddScoped<AddBlogCommentHandler>();
        services.AddScoped<ToggleBlogReactionHandler>();

        services.AddScoped<ListBlogCommentsHandler>();
        services.AddScoped<GetBlogReactionsHandler>();

        return services;
    }
}
