using StrongTypes;

namespace Kalandra.McpServer.Infrastructure;

/// <summary>
/// ResourceUri is the OAuth resource identifier (RFC 8707/9728) clients request a token for; a token minted for another resource isn't meant for this server.
/// </summary>
public record McpServerConfig(Uri ResourceUri)
{
    public static McpServerConfig AddSingleton(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var resourceUrl = NonEmptyString.Create(configuration.GetSection("Mcp")["ResourceUri"]).Value;

        if (!environment.IsDevelopment()
            && (resourceUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) || resourceUrl.Contains("127.0.0.1")))
            throw new InvalidOperationException(
                "Mcp:ResourceUri still points at localhost — a production deploy must set the real public URL.");

        var config = new McpServerConfig(new Uri(resourceUrl));
        services.AddSingleton(config);
        return config;
    }
}
