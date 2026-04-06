using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record TurnstileConfig
{
    public NonEmptyString SecretKey { get; private init; } = null!;

    public static TurnstileConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Turnstile");

        var config = new TurnstileConfig
        {
            SecretKey = NonEmptyString.CreateUnsafe(section["SecretKey"])
        };

        services.AddSingleton(config);

        return config;
    }
}
