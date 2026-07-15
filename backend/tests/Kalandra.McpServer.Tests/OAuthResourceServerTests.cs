using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kalandra.Blog.Feed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.McpServer.Tests;

/// <summary>
/// Boots the MCP host without real infrastructure: the database config is never dialled (the anonymous
/// tier never opens a session) and the blog RSS feed is stubbed in-process, so the public tools can be
/// exercised end to end while the OAuth behaviour stays pinned by pure HTTP assertions.
/// </summary>
public class McpServerFactory : WebApplicationFactory<Program>
{
    public const string ResourceUri = "https://mcp.test.local";
    public const string SupabaseUrl = "https://test-project.supabase.co";
    public const string StubBlogPostSlug = "strongly-typed-ids";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development keeps the production-only guards (Sentry, localhost checks) out of the test host.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Port=1;Database=unused;Username=u;Password=p");
        builder.UseSetting("Supabase:ProjectUrl", SupabaseUrl);
        builder.UseSetting("Supabase:ServiceKey", "test-service-key");
        builder.UseSetting("Mcp:ResourceUri", ResourceUri);
        builder.UseSetting("BlogFeed:RssUrl", "http://localhost:4321/rss.xml");

        builder.ConfigureTestServices(services =>
            services.AddHttpClient<BlogFeedClient>().ConfigurePrimaryHttpMessageHandler(() => new StubRssFeedHandler()));
    }

    private sealed class StubRssFeedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var rss = $"""
                <rss version="2.0"><channel><title>kalandra.tech blog</title>
                <item>
                  <title>[EN] Strongly Typed IDs</title>
                  <description>Why strongly typed ids beat raw Guids.</description>
                  <link>https://www.kalandra.tech/blog/{StubBlogPostSlug}</link>
                  <pubDate>Tue, 01 Jul 2026 08:00:00 GMT</pubDate>
                  <category>dotnet</category>
                </item>
                </channel></rss>
                """;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(rss, Encoding.UTF8, "application/rss+xml"),
            };
            return Task.FromResult(response);
        }
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
    public async Task Mcp_WithAnInvalidToken_ChallengesWithTheResourceMetadataUrl()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-token");

        var response = await client.SendAsync(request, Ct);

        // A presented-but-invalid token must get the OAuth challenge, not a silent downgrade to the
        // anonymous tier — the 401 is how a client with an expired token learns to re-authenticate.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = string.Join(", ", response.Headers.WwwAuthenticate);
        Assert.Contains("resource_metadata", challenge);
        Assert.Contains("oauth-protected-resource", challenge);
    }
}
