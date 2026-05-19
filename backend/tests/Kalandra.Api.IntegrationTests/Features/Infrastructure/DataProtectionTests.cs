using Kalandra.Api.Infrastructure.DataProtection;
using Kalandra.Api.IntegrationTests.Helpers;
using Marten;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.Api.IntegrationTests.Features.Infrastructure;

public class DataProtectionTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Protect_Unprotect_RoundTrips_And_PersistsKey()
    {
        using var scope = factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("test-purpose");

        var protectedText = protector.Protect("hello");
        var unprotectedText = protector.Unprotect(protectedText);

        Assert.Equal("hello", unprotectedText);

        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var session = store.QuerySession();
        var keyCount = await session.Query<DataProtectionKey>().CountAsync(Ct);
        Assert.True(keyCount > 0, "Expected at least one key to be persisted in Marten.");
    }
}
