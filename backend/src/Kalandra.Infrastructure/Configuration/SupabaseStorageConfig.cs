using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record SupabaseStorageConfig
{
    public NonEmptyString ProjectUrl { get; }
    public NonEmptyString BucketName { get; }
    public NonEmptyString ServiceKey { get; }

    private SupabaseStorageConfig(
        NonEmptyString projectUrl,
        NonEmptyString bucketName,
        NonEmptyString serviceKey)
    {
        ProjectUrl = projectUrl;
        BucketName = bucketName;
        ServiceKey = serviceKey;
    }

    public static SupabaseStorageConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage");

        var config = new SupabaseStorageConfig(
            projectUrl: NonEmptyString.CreateUnsafe(section["SupabaseProjectUrl"]),
            bucketName: NonEmptyString.CreateUnsafe(section["BucketName"]),
            serviceKey: NonEmptyString.CreateUnsafe(section["ServiceKey"]));

        services.AddSingleton(config);

        return config;
    }
}
