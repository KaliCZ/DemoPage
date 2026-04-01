using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record SupabaseStorageConfig
{
    public NonEmptyString BucketName { get; }
    public NonEmptyString ServiceKey { get; }

    private SupabaseStorageConfig(NonEmptyString bucketName, NonEmptyString serviceKey)
    {
        BucketName = bucketName;
        ServiceKey = serviceKey;
    }

    public static SupabaseStorageConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage");

        var config = new SupabaseStorageConfig(
            bucketName: NonEmptyString.CreateUnsafe(section["BucketName"]),
            serviceKey: NonEmptyString.CreateUnsafe(section["ServiceKey"]));

        services.AddSingleton(config);

        return config;
    }
}
