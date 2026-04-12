using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Infrastructure.Configuration;

public record SupabaseStorageConfig(NonEmptyString BucketName)
{
    public static SupabaseStorageConfig AddSingleton(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage");

        var config = new SupabaseStorageConfig(
            BucketName: NonEmptyString.CreateUnsafe(section["BucketName"]));

        services.AddSingleton(config);

        return config;
    }
}
