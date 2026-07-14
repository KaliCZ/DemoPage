using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.Hosting;

/// <summary>
/// Configuration for the BetterStack OTLP exporter.
/// Not currently used — observability has been consolidated onto Sentry (see <see cref="SentryConfig"/>).
/// Kept as nullable/optional so the wiring can be re-enabled without re-introducing the plumbing.
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
