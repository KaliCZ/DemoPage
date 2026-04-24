using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record SupabaseConfig(
    NonEmptyString ProjectUrl,
    NonEmptyString ServiceKey)
{
    public static SupabaseConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Supabase");

        var config = new SupabaseConfig(
            ProjectUrl: NonEmptyString.Create(section["ProjectUrl"]),
            ServiceKey: NonEmptyString.Create(section["ServiceKey"]));

        services.AddSingleton(config);

        return config;
    }
}
