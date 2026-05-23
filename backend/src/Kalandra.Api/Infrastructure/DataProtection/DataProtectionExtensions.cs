using Marten;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace Kalandra.Api.Infrastructure.DataProtection;

public static class DataProtectionExtensions
{
    // ApplicationName must be stable across deploys; otherwise rotated keys end up
    // siloed under different discriminators and old payloads silently fail to decrypt.
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
