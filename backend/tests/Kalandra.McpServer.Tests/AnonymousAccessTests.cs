using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kalandra.McpServer.Tests;

/// <summary>
/// Pins the anonymous tier: without a token — or with one that doesn't validate — the endpoint connects
/// and serves the public blog tools, while the account tools stay hidden from tools/list and refuse
/// direct calls.
/// </summary>
public class AnonymousAccessTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialize_WithoutAToken_Succeeds()
    {
        var response = await PostMcp(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0"}}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonRpcResponse(response);
        var serverInfo = document.RootElement.GetProperty("result").GetProperty("serverInfo");
        Assert.Equal("kalandra-tech", serverInfo.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ToolsList_WithoutAToken_ContainsExactlyThePublicBlogTools()
    {
        var response = await PostMcp("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonRpcResponse(response);
        var toolNames = document.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).Order().ToList();
        Assert.Equal(["get_blog_post_comments", "list_blog_posts"], toolNames);
    }

    [Fact]
    public async Task ListBlogPosts_WithoutAToken_ReturnsThePostsWithTotalsButNoReadState()
    {
        var response = await PostMcp(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_blog_posts","arguments":{}}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonRpcResponse(response);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean());

        var text = result.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString()!;
        using var postsDocument = JsonDocument.Parse(text);
        var post = postsDocument.RootElement.EnumerateArray().Single();

        Assert.Equal(McpServerFactory.StubBlogPostSlug, post.GetProperty("slug").GetString());
        // The blog-index totals are served to everyone (zero here: the stub slug has no catalog entry).
        Assert.Equal(0, post.GetProperty("totalViews").GetInt32());
        Assert.Equal(0, post.GetProperty("totalReactions").GetInt32());
        Assert.Equal(0, post.GetProperty("totalComments").GetInt32());
        // Anonymous callers have no reading history, so the per-viewer read state must stay unset.
        Assert.True(!post.TryGetProperty("viewerViews", out var viewerViews) || viewerViews.ValueKind == JsonValueKind.Null);
        Assert.True(!post.TryGetProperty("watched", out var watched) || watched.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task AccountToolCall_WithoutAToken_IsRefused()
    {
        var response = await PostMcp(
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"submit_job_offer","arguments":{}}}""");

        using var document = await ReadJsonRpcResponse(response);
        var message = document.RootElement.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("requires authorization", message);
    }

    [Fact]
    public async Task ToolsList_WithAnInvalidToken_IsServedAsAnonymous()
    {
        // The accepted trade-off of the anonymous tier: a bad or expired token authenticates as nobody
        // and gets the public tools rather than a 401 — keeping the token fresh is the client's job.
        var response = await PostMcp(
            """{"jsonrpc":"2.0","id":5,"method":"tools/list","params":{}}""", bearerToken: "not-a-valid-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadJsonRpcResponse(response);
        var toolNames = document.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).Order().ToList();
        Assert.Equal(["get_blog_post_comments", "list_blog_posts"], toolNames);
    }

    private async Task<HttpResponseMessage> PostMcp(string jsonRpc, string? bearerToken = null)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(jsonRpc, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (bearerToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await client.SendAsync(request, Ct);
    }

    // The streamable HTTP transport answers a POST either as bare JSON or as an SSE stream carrying
    // the JSON-RPC response in a "data:" line — accept both shapes.
    private static async Task<JsonDocument> ReadJsonRpcResponse(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? body.Split('\n').First(line => line.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..]
            : body;
        return JsonDocument.Parse(json);
    }
}
