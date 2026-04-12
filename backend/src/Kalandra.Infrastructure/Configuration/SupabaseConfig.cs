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
            ProjectUrl: NonEmptyString.CreateUnsafe(section["ProjectUrl"]),
            ServiceKey: NonEmptyString.CreateUnsafe(section["ServiceKey"]));

        services.AddSingleton(config);

        return config;
    }
}
