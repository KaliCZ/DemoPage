using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record SupabaseConfig(
    NonEmptyString ProjectUrl,
    NonEmptyString ServiceKey)
{
    public static SupabaseConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection("Supabase");

        var config = new SupabaseConfig(
            ProjectUrl: NonEmptyString.Create(section["ProjectUrl"]),
            ServiceKey: NonEmptyString.Create(section["ServiceKey"]));

        var projectUrl = config.ProjectUrl.Value;
        if (!environment.IsDevelopment()
            && (projectUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) || projectUrl.Contains("127.0.0.1")))
            throw new InvalidOperationException(
                "Supabase:ProjectUrl still points at localhost — a production deploy must set the real project URL.");

        services.AddSingleton(config);

        return config;
    }
}
