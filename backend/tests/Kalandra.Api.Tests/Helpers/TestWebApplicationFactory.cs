using Kalandra.Infrastructure.Configuration;
using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.PostgreSql;

namespace Kalandra.Api.Tests.Helpers;

public class TestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        builder.UseSetting("Auth:SupabaseProjectUrl", "https://test-project.supabase.co");
        builder.UseSetting("Auth:ServiceKey", "test-service-key");
        builder.UseSetting("Storage:BucketName", "test-bucket");
        builder.UseSetting("Storage:ServiceKey", "test-service-key");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IStorageService>();
            services.AddSingleton<IStorageService, InMemoryStorageService>();

            services.RemoveAll<SupabaseUserService>();
            services.AddSingleton<SupabaseUserService>(sp =>
                new SupabaseUserService(
                    new HttpClient(new NoOpAvatarHandler()),
                    sp.GetRequiredService<SupabaseAuthConfig>(),
                    sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SupabaseUserService>>()));

            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    var handler = new FakeJwksHandler(JwtTestHelper.TestIssuer);
                    var httpClient = new HttpClient(handler);
                    var metadataAddress = $"{JwtTestHelper.TestIssuer}/.well-known/openid-configuration";

                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        metadataAddress: metadataAddress,
                        configRetriever: new OpenIdConnectConfigurationRetriever(),
                        docRetriever: new HttpDocumentRetriever(httpClient) { RequireHttps = true });
                });
        });
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
