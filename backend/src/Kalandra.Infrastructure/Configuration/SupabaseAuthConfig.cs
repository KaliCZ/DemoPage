using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record SupabaseAuthConfig
{
    public NonEmptyString ProjectUrl { get; }
    public NonEmptyString ServiceKey { get; }

    private SupabaseAuthConfig(
        NonEmptyString projectUrl,
        NonEmptyString serviceKey)
    {
        ProjectUrl = projectUrl;
        ServiceKey = serviceKey;
    }

    public static SupabaseAuthConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Auth");

        var config = new SupabaseAuthConfig(
            projectUrl: NonEmptyString.CreateUnsafe(section["SupabaseProjectUrl"]),
            serviceKey: NonEmptyString.CreateUnsafe(section["ServiceKey"]));

        services.AddSingleton(config);

        return config;
    }
}
