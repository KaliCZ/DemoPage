using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record TemporalConfig(NonEmptyString TargetHost, NonEmptyString Namespace)
{
    public static TemporalConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Temporal");

        var config = new TemporalConfig(
            TargetHost: NonEmptyString.Create(section["TargetHost"]),
            Namespace: NonEmptyString.Create(section["Namespace"]));

        services.AddSingleton(config);

        return config;
    }
}
