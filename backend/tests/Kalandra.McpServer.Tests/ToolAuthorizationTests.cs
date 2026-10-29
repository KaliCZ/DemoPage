using System.Net;
using System.Net.Http.Headers;

namespace Kalandra.McpServer.Tests;

/// <summary>Calls every listed tool anonymously, so an account tool that slips into the public set fails here instead of leaking.</summary>
public class ToolAuthorizationTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    // Deliberately a copy of McpAccountGate.PublicTools: reusing the gate's own set would assert nothing.
    private static readonly string[] PublicTools = ["get_blog_post_comments", "list_blog_posts"];

    // Valid enough to reach the tool body, so a served call really runs rather than failing parameter binding.
    private static readonly Dictionary<string, string> ArgumentsByTool = new()
    {
        ["list_blog_posts"] = "{}",
        ["get_blog_post_comments"] = $$"""{"slug":"{{McpServerFactory.StubBlogPostSlug}}"}""",
        ["post_blog_comment"] = $$"""{"slug":"{{McpServerFactory.StubBlogPostSlug}}","content":"Nice post."}""",
        ["get_my_comments"] = "{}",
        ["list_my_job_offers"] = "{}",
        ["submit_job_offer"] = """{"companyName":"Contoso","contactName":"Jane Doe","contactEmail":"jane@contoso.com","jobTitle":"CTO","description":"Lead the team.","isRemote":true}""",
        ["get_job_offer_comments"] = """{"jobOfferId":"11111111-1111-1111-1111-111111111111"}""",
        ["add_job_offer_comment"] = """{"jobOfferId":"11111111-1111-1111-1111-111111111111","content":"Any news?"}""",
    };

    private const string AuthorizedMarker = "[Authorized]";

    [Fact]
    public async Task EveryListedTool_MarksAndEnforcesItsTierConsistently()
    {
        var tools = await ListTools();
        Assert.NotEmpty(tools);

        foreach (var (name, description) in tools)
        {
            Assert.True(ArgumentsByTool.ContainsKey(name), $"'{name}' is new — add arguments for it so this sweep can call it.");

            var isPublic = PublicTools.Contains(name);
            var marked = description.StartsWith(AuthorizedMarker, StringComparison.Ordinal);
            Assert.True(isPublic != marked,
                $"'{name}' is {Tier(isPublic)} but its description {(marked ? "carries" : "lacks")} the {AuthorizedMarker} marker.");

            var response = await CallAnonymously(name);
            var challenged = response.StatusCode == HttpStatusCode.Unauthorized;
            Assert.True(isPublic != challenged,
                $"'{name}' is {Tier(isPublic)} but an anonymous call was answered {(int)response.StatusCode}.");
        }
    }

    [Fact]
    public async Task ToolsList_WithoutAToken_StillOffersTheAccountTools()
    {
        // A model can only tell the user what the site offers if it sees the account tools before sign-in.
        var names = (await ListTools()).Select(tool => tool.Name).ToList();
        Assert.Contains("submit_job_offer", names);
        Assert.Contains("get_my_comments", names);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stale-or-forged-token")]
    public async Task AnAccountTool_WithoutAUsableToken_ChallengesTowardsSupabase(string? bearerToken)
    {
        // Only a 401 lets the client's OAuth code sign in or refresh on its own; a "please sign in" tool error can't.
        var response = await CallAccountTool(bearerToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("resource_metadata=", challenge.Parameter);
        // The spec's SHOULD: the challenge itself names the scopes to request, sparing the client a guess.
        Assert.Contains("scope=\"openid email profile\"", challenge.Parameter);
    }

    [Fact]
    public async Task ARejectedToken_IsNamedTheProblem_SoTheClientRefreshesIt()
    {
        // RFC 6750's error="invalid_token" is what tells a client to refresh rather than start a new sign-in.
        var response = await CallAccountTool("stale-or-forged-token");

        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Contains("error=\"invalid_token\"", challenge.Parameter);
    }

    [Theory]
    // Scanners send these anonymously; naming no public tool, they must fail closed to the challenge, not a 500 or an SDK alert.
    [InlineData("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":null}""")]
    [InlineData("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":[]}""")]
    [InlineData("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":"get_my_comments"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":123}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":6,"method":"tools/call"}""")]
    public async Task AMisshapenToolCall_WithoutAToken_IsChallengedLikeAnyAccountCall(string jsonRpc)
    {
        var response = await factory.PostMcp(jsonRpc);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
    }

    [Fact]
    public async Task AFirstVisitWithNoToken_GetsAPlainChallenge_NotAnErrorCode()
    {
        // RFC 6750: with no credentials presented, the challenge SHOULD NOT carry an error code.
        var response = await CallAccountTool(bearerToken: null);

        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.DoesNotContain("error=", challenge.Parameter);
    }

    [Fact]
    public async Task TheDiscoveryDocument_StaysReachable_WithTheVeryTokenThatWasRejected()
    {
        // Challenging the discovery document too would trap a client holding a stale token in a loop.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "stale-token");

        var response = await client.GetAsync("/.well-known/oauth-protected-resource/mcp", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string Tier(bool isPublic) => isPublic ? "public" : "account-only";

    private async Task<List<(string Name, string Description)>> ListTools()
    {
        var response = await factory.PostMcp("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""");
        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
        return [.. document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => (tool.GetProperty("name").GetString()!, tool.GetProperty("description").GetString() ?? ""))];
    }

    private Task<HttpResponseMessage> CallAccountTool(string? bearerToken) =>
        factory.PostMcp(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_my_comments","arguments":{}}}""",
            bearerToken);

    private Task<HttpResponseMessage> CallAnonymously(string toolName) =>
        factory.PostMcp(
            $$$"""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"{{{toolName}}}","arguments":{{{ArgumentsByTool[toolName]}}}}}""");
}
