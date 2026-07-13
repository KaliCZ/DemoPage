using Kalandra.McpServer.Infrastructure;
using Kalandra.McpServer.Tools;

namespace Kalandra.McpServer;

public static class McpRegistration
{
    /// <summary>
    /// Registers the MCP server and its tools. The tools reuse the same domain handlers and Marten store
    /// as the REST API, acting as the request's OAuth-authenticated user — one domain, two front doors.
    /// </summary>
    public static IServiceCollection AddMcpTools(this IServiceCollection services)
    {
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "kalandra-tech", Version = AppVersion.InformationalVersion };
                options.ServerInstructions =
                    "Tools for interacting with Pavel Kalandra's showcase site kalandra.tech: submit and follow up on " +
                    "job offers, browse blog posts, and read or write comments — all acting as the signed-in user.";
            })
            // Stateless: every tool call is a self-contained POST carrying the caller's bearer token,
            // so no session affinity is needed behind the blue/green proxy.
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<JobOfferMcpTools>()
            .WithTools<BlogMcpTools>();

        return services;
    }
}
