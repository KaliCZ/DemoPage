using Kalandra.Blog;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Email;
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
    public TestEmailSender EmailSender { get; } = new();
    public FakeUserInfoService UserInfoService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Mirror the dev/e2e environment so the configs' prod-only localhost checks stay out of the test host.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        builder.UseSetting("Supabase:ProjectUrl", "https://test-project.supabase.co");
        builder.UseSetting("Supabase:ServiceKey", "test-service-key");
        builder.UseSetting("Blog:AuthorNotificationEmail", "author@kalandra.local");
        builder.UseSetting("JobOffers:OwnerNotificationEmail", "owner@kalandra.local");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);

            // Random per-test slugs need to resolve; the prod catalog gates to real posts.
            services.RemoveAll<IBlogPostCatalog>();
            services.AddSingleton<IBlogPostCatalog, TestBlogPostCatalog>();

            services.RemoveAll<IStorageService>();
            services.AddSingleton<IStorageService, InMemoryStorageService>();

            services.RemoveAll<ITurnstileValidator>();
            services.AddSingleton<ITurnstileValidator, AlwaysPassTurnstileValidator>();

            services.RemoveAll<ISupabaseAdminService>();
            services.AddSingleton<ISupabaseAdminService>(FakeAdminService);

            services.RemoveAll<IUserInfoService>();
            services.AddSingleton<IUserInfoService>(UserInfoService);

            services.RemoveAll<Supabase.Storage.Client>();
            services.RemoveAll<Supabase.Gotrue.Interfaces.IGotrueAdminClient<Supabase.Gotrue.User>>();

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
        // Stop the host — and its async daemon — before the database it talks to, so daemon shutdown
        // doesn't log connection failures into an already-disposed logger and fault the fixture teardown.
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
