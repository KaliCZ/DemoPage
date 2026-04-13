namespace Kalandra.Api.Infrastructure;

public record BetterStackConfig(
    NonEmptyString SourceToken,
    Uri OtlpEndpoint)
{
    public string AuthorizationHeader => $"Authorization=Bearer {SourceToken}";

    public static BetterStackConfig? AddOptionalSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("BetterStack");

        if (section["SourceToken"].AsNonEmpty().GetOrNull() is not { } sourceToken ||
            section["OtlpEndpoint"].AsNonEmpty().GetOrNull() is not { } otlpEndpoint)
        {
            return null;
        }

        var config = new BetterStackConfig(SourceToken: sourceToken, OtlpEndpoint: new Uri(otlpEndpoint.Value));

        services.AddSingleton(config);

        return config;
    }
}
