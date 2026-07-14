using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kalandra.McpServer.Tests;

/// <summary>
/// Boots the MCP host with config that is never dialled: an unauthenticated call is answered by the
/// OAuth challenge before any token validation, Supabase call, or database work happens — which is
/// exactly the resource-server behaviour these tests pin down.
/// </summary>
public class McpServerFactory : WebApplicationFactory<Program>
{
    public const string ResourceUri = "https://mcp.test.local";
    public const string SupabaseUrl = "https://test-project.supabase.co";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development keeps the production-only guards (Sentry, localhost checks) out of the test host.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Port=1;Database=unused;Username=u;Password=p");
        builder.UseSetting("Supabase:ProjectUrl", SupabaseUrl);
        builder.UseSetting("Supabase:ServiceKey", "test-service-key");
        builder.UseSetting("Mcp:ResourceUri", ResourceUri);
        builder.UseSetting("BlogFeed:RssUrl", "http://localhost:4321/rss.xml");
    }
}

public class OAuthResourceServerTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProtectedResourceMetadata_NamesThisServerAndPointsAtSupabase()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/oauth-protected-resource", Ct);

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        // The canonical resource id an assistant asks Supabase for a token against (RFC 8707/9728).
        Assert.Equal(McpServerFactory.ResourceUri, document.RootElement.GetProperty("resource").GetString());

        var authorizationServers = document.RootElement.GetProperty("authorization_servers")
            .EnumerateArray().Select(element => element.GetString()).ToList();
        Assert.Contains($"{McpServerFactory.SupabaseUrl}/auth/v1", authorizationServers);
    }

    [Fact]
    public async Task Mcp_WithoutAToken_ChallengesWithTheResourceMetadataUrl()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"), Ct);

        // The 401 + resource_metadata pointer is what makes a client discover Supabase and sign the user in.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = string.Join(", ", response.Headers.WwwAuthenticate);
        Assert.Contains("resource_metadata", challenge);
        Assert.Contains("oauth-protected-resource", challenge);
    }
}
