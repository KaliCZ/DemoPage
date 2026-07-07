using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record TurnstileConfig(NonEmptyString SecretKey)
{
    // Cloudflare's "always passes" test secret; a production deploy must override it.
    private const string TestSecretKey = "1x0000000000000000000000000000000AA";

    public static TurnstileConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection("Turnstile");

        var config = new TurnstileConfig(
            SecretKey: NonEmptyString.Create(section["SecretKey"]));

        if (!environment.IsDevelopment() && config.SecretKey.Value == TestSecretKey)
            throw new InvalidOperationException(
                "Turnstile:SecretKey is still the shared test key — a production deploy must set the real secret.");

        services.AddSingleton(config);

        return config;
    }
}
