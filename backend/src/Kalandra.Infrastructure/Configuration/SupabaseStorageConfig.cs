using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record SupabaseStorageConfig
{
    public NonEmptyString ProjectUrl { get; private init; } = null!;
    public NonEmptyString BucketName { get; private init; } = null!;
    public NonEmptyString ServiceKey { get; private init; } = null!;

    public static SupabaseStorageConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage");

        var config = new SupabaseStorageConfig
        {
            ProjectUrl = NonEmptyString.CreateUnsafe(section["SupabaseProjectUrl"]),
            BucketName = NonEmptyString.CreateUnsafe(section["BucketName"]),
            ServiceKey = NonEmptyString.CreateUnsafe(section["ServiceKey"])
        };

        services.AddSingleton(config);

        return config;
    }
}
