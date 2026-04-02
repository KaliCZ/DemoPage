using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record SupabaseAuthConfig
{
    public NonEmptyString ProjectUrl { get; }

    private SupabaseAuthConfig(NonEmptyString projectUrl)
    {
        ProjectUrl = projectUrl;
    }

    public static SupabaseAuthConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Auth");

        var config = new SupabaseAuthConfig(
            projectUrl: NonEmptyString.CreateUnsafe(section["SupabaseProjectUrl"]));

        services.AddSingleton(config);

        return config;
    }
}
