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
/// Boots the MCP host without real infrastructure: the blog RSS feed is stubbed in-process, and its slug
/// is deliberately absent from the backend post catalog so the stats batch stays empty and the dead
/// database config is never dialled — the public tools run end to end on pure HTTP assertions.
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

    public async Task<HttpResponseMessage> PostMcp(string jsonRpc, string? bearerToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(jsonRpc, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (bearerToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    // The streamable HTTP transport answers a POST either as bare JSON or as an SSE stream carrying
    // the JSON-RPC response in a "data:" line — accept both shapes.
    public static async Task<JsonDocument> ReadJsonRpcResponse(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? body.Split('\n').First(line => line.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..]
            : body;
        return JsonDocument.Parse(json);
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

}
