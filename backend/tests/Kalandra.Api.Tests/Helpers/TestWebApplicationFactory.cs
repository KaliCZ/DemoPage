using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Kalandra.Api.Tests.Helpers;

public class TestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Override Marten to use the test database
            services.Configure<StoreOptions>(options =>
            {
                options.Connection(_postgres.GetConnectionString());
            });
        });

        builder.UseSetting("ConnectionStrings:DefaultConnection", "will-be-overridden");
        builder.UseSetting("Auth:SupabaseProjectUrl", "https://test-project.supabase.co");
        builder.UseSetting("Auth:SupabaseJwtSecret", JwtTestHelper.TestSecret);
        builder.UseSetting("Auth:AdminUserIds:0", "admin-user-id");
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
