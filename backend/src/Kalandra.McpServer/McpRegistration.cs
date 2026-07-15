using Kalandra.Hosting;
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
                    "Tools for interacting with Pavel Kalandra's showcase site kalandra.tech. Browsing blog posts " +
                    "and reading their comments works without signing in. Submitting and following up on job offers " +
                    "and writing comments act as the signed-in user — those tools appear once the user authenticates " +
                    "this server with their kalandra.tech account (OAuth). If the user asks for one of them, tell " +
                    "them to sign this server in from their assistant's connector settings.";
            })
            // Stateless: every tool call is a self-contained POST carrying the caller's bearer token,
            // so no session affinity is needed behind the blue/green proxy.
            .WithHttpTransport(transport => transport.Stateless = true)
            // Enforces the tools' [Authorize]/[AllowAnonymous] attributes and filters tools/list by them,
            // so anonymous callers only see the public tools.
            .AddAuthorizationFilters()
            .WithTools<JobOfferMcpTools>()
            .WithTools<BlogMcpTools>();

        return services;
    }
}
