using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

// Temporal runs co-located on the production VM, so localhost:7233 is its correct
// production target — there is no localhost check to make here.
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
