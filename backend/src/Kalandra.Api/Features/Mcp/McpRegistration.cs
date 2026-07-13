using Kalandra.Api.Infrastructure;

namespace Kalandra.Api.Features.Mcp;

public static class McpRegistration
{
    /// <summary>
    /// Registers the MCP server and its tools alongside the REST controllers. The tools run in the
    /// same process and DI container as the controllers, so they reuse the domain handlers, auth
    /// (<c>ICurrentUserAccessor</c> reads the validated bearer token), Marten, and the notification
    /// subscriptions — no separate host, no second write path.
    /// </summary>
    public static IServiceCollection AddMcp(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        BlogFeedConfig.AddSingleton(services, configuration, environment);
        services.AddHttpClient<BlogFeedClient>();

        services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "kalandra-tech", Version = AppVersion.InformationalVersion };
                options.ServerInstructions =
                    "Tools for interacting with Pavel Kalandra's showcase site kalandra.tech: submit and follow up on " +
                    "job offers, browse blog posts, and read or write comments. Write tools act as the signed-in user " +
                    "and need a Supabase access token in the connection's Authorization header; reading blog posts " +
                    "and their comments works without one.";
            })
            // Stateless: every tool call is a self-contained POST carrying the caller's bearer token,
            // so no session affinity is needed behind the blue/green proxy.
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<JobOfferMcpTools>()
            .WithTools<BlogMcpTools>();

        return services;
    }
}
