using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Turnstile;
using Kalandra.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.PostgreSql;

namespace Kalandra.Api.IntegrationTests.Helpers;

public class TestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public FakeSupabaseAdminService FakeAdminService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        builder.UseSetting("Supabase:ProjectUrl", "https://test-project.supabase.co");
        builder.UseSetting("Supabase:ServiceKey", "test-service-key");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IStorageService>();
            services.AddSingleton<IStorageService, InMemoryStorageService>();

            services.RemoveAll<ITurnstileValidator>();
            services.AddSingleton<ITurnstileValidator, AlwaysPassTurnstileValidator>();

            services.RemoveAll<ISupabaseAdminService>();
            services.AddSingleton<ISupabaseAdminService>(FakeAdminService);

            services.RemoveAll<IUserInfoService>();
            services.AddSingleton<IUserInfoService, NoOpUserInfoService>();

            services.RemoveAll<Supabase.Client>();

            // The Supabase auth/storage health checks depend on the real Supabase.Client,
            // which we removed above. Drop them from the /health endpoint so tests stay green;
            // production wiring still registers them in Program.cs.
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Remove(options.Registrations.Single(r => r.Name == "supabase-auth"));
                options.Registrations.Remove(options.Registrations.Single(r => r.Name == "supabase-storage"));
            });

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

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        // Bypass hire-me rate limiter in tests — same mechanism the frontend uses
        // after the user solves the interactive Turnstile challenge.
        client.DefaultRequestHeaders.Add("X-Interactive-Captcha", "1");
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
