using Marten;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace Kalandra.Api.Infrastructure.DataProtection;

public static class DataProtectionExtensions
{
    // Must stay stable across deploys, or keys land under a new discriminator and old payloads silently fail to decrypt.
    private const string ApplicationName = "kalandra-api";

    public static IServiceCollection AddAppDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName(ApplicationName);

        services.AddOptions<KeyManagementOptions>()
            .Configure<IDocumentStore>((options, store) =>
            {
                options.XmlRepository = new MartenXmlRepository(store);
            });

        return services;
    }
}
