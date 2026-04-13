namespace Kalandra.Api.Infrastructure;

public record SentryConfig(
    NonEmptyString Dsn)
{
    public static SentryConfig? AddOptionalSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Sentry");

        if (section["Dsn"].AsNonEmpty().GetOrNull() is not { } dsn)
            return null;

        var config = new SentryConfig(Dsn: dsn);

        services.AddSingleton(config);

        return config;
    }
}
