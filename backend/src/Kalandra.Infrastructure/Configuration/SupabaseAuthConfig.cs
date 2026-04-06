using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record SupabaseAuthConfig
{
    public NonEmptyString ProjectUrl { get; private init; } = null!;
    public NonEmptyString ServiceKey { get; private init; } = null!;

    public static SupabaseAuthConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Auth");

        var config = new SupabaseAuthConfig
        {
            ProjectUrl = NonEmptyString.CreateUnsafe(section["SupabaseProjectUrl"]),
            ServiceKey = NonEmptyString.CreateUnsafe(section["ServiceKey"])
        };

        services.AddSingleton(config);

        return config;
    }
}
