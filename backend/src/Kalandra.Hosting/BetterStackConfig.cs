using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.Hosting;

/// <summary>
/// Dormant while Sentry is the active backend; optional so the BetterStack exporters can be re-enabled by configuration alone.
/// </summary>
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

        if (section["SourceToken"].AsNonEmpty() is not { } sourceToken ||
            section["OtlpEndpoint"].AsNonEmpty() is not { } otlpEndpoint)
        {
            return null;
        }

        var config = new BetterStackConfig(SourceToken: sourceToken, OtlpEndpoint: new Uri(otlpEndpoint));

        services.AddSingleton(config);

        return config;
    }
}
