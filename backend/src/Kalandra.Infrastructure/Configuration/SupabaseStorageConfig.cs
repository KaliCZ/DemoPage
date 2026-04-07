using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record SupabaseStorageConfig(
    NonEmptyString ProjectUrl,
    NonEmptyString BucketName,
    NonEmptyString ServiceKey)
{
    public static SupabaseStorageConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage");

        var config = new SupabaseStorageConfig(
            ProjectUrl: NonEmptyString.CreateUnsafe(section["SupabaseProjectUrl"]),
            BucketName: NonEmptyString.CreateUnsafe(section["BucketName"]),
            ServiceKey: NonEmptyString.CreateUnsafe(section["ServiceKey"]));

        services.AddSingleton(config);

        return config;
    }
}
