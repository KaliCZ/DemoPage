using System.Net;
using System.Text.Json;

namespace Kalandra.McpServer.Tests;

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
        // Zero because the stub slug has no catalog entry.
        Assert.Equal(0, post.GetProperty("totalViews").GetInt32());
        Assert.Equal(0, post.GetProperty("totalReactions").GetInt32());
        Assert.Equal(0, post.GetProperty("totalComments").GetInt32());
        Assert.True(!post.TryGetProperty("viewerViews", out var viewerViews) || viewerViews.ValueKind == JsonValueKind.Null);
        Assert.True(!post.TryGetProperty("watched", out var watched) || watched.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task AnyCall_WithAnInvalidToken_IsChallengedRatherThanDowngraded()
    {
        // The MCP authorization spec requires a 401 for an invalid token even on a public tool, so the client refreshes.
        var response = await factory.PostMcp(
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"list_blog_posts","arguments":{}}}""",
            bearerToken: "not-a-valid-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
