using StrongTypes;

namespace Kalandra.McpServer.Infrastructure;

public record SentryConfig(NonEmptyString Dsn)
{
    public static SentryConfig? AddOptionalSingleton(IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetSection("Sentry")["Dsn"].AsNonEmpty() is not { } dsn)
            return null;

        var config = new SentryConfig(Dsn: dsn);
        services.AddSingleton(config);
        return config;
    }
}
