using System.Net;
using System.Text.Json;

namespace Kalandra.McpServer.Tests;

/// <summary>
/// Pins the anonymous tier: without a token — or with one that doesn't validate — the endpoint connects and
/// serves the public blog tools. Which tools are offered and which refuse an anonymous caller is swept by
/// <see cref="ToolAuthorizationTests"/>.
/// </summary>
public class AnonymousAccessTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    [Fact]
    public async Task Initialize_WithoutAToken_Succeeds()
    {
        var response = await factory.PostMcp(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0"}}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
        var serverInfo = document.RootElement.GetProperty("result").GetProperty("serverInfo");
        Assert.Equal("kalandra-tech", serverInfo.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ToolsList_WithoutAToken_ContainsTheWholeToolset()
    {
        var response = await factory.PostMcp("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
        var toolNames = document.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).Order().ToList();
        Assert.Equal([
            "add_job_offer_comment", "get_blog_post_comments", "get_job_offer_comments", "get_my_comments",
            "list_blog_posts", "list_my_job_offers", "post_blog_comment", "submit_job_offer",
        ], toolNames);
    }

    [Fact]
    public async Task ListBlogPosts_WithoutAToken_ReturnsThePostsWithTotalsButNoReadState()
    {
        var response = await factory.PostMcp(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_blog_posts","arguments":{}}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
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
    public async Task AccountToolCall_WithoutAToken_IsRefusedAsAToolErrorTheModelCanRead()
    {
        var response = await factory.PostMcp(
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_my_comments","arguments":{}}}""");

        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
        var result = document.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var text = result.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString();
        Assert.Contains("kalandra.tech account", text);
    }

    [Fact]
    public async Task AnAccountTool_WithAnInvalidToken_IsServedAsAnonymous()
    {
        // The accepted trade-off of the anonymous tier: a bad or expired token authenticates as nobody
        // and gets served rather than challenged — keeping the token fresh is the client's job.
        var response = await factory.PostMcp(
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"get_my_comments","arguments":{}}}""",
            bearerToken: "not-a-valid-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
        var text = document.RootElement.GetProperty("result").GetProperty("content")
            .EnumerateArray().First().GetProperty("text").GetString();
        Assert.Contains("kalandra.tech account", text);
    }
}
