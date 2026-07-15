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
                    "and reading their comments works without signing in. Tools marked [Authorized] act as the " +
                    "signed-in user — submitting and following up on job offers, and writing comments. They are " +
                    "listed even when nobody is signed in, so you can tell the user what this site offers; calling " +
                    "one without an account returns a message saying so. If the user wants one, tell them to sign " +
                    "this server in with their kalandra.tech account from their assistant's connector settings.";
            })
            // Stateless: every tool call is a self-contained POST carrying the caller's bearer token,
            // so no session affinity is needed behind the blue/green proxy.
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<JobOfferMcpTools>()
            .WithTools<BlogMcpTools>();

        return services;
    }
}
