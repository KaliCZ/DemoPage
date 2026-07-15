namespace Kalandra.McpServer.Tests;

/// <summary>
/// The account gate lives in each tool's RequireUser call, not in a framework filter, so this is what keeps it
/// honest: every tool the server lists is really called without a token and must behave the way its description
/// advertises. A new account tool that forgets RequireUser fails here instead of leaking.
/// </summary>
public class ToolAuthorizationTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    // The only tools allowed to answer an anonymous caller; anything else must refuse one. A new tool is
    // account-only by omission — the safe default, since forgetting to list it here can only over-restrict.
    private static readonly string[] PublicTools = ["get_blog_post_comments", "list_blog_posts"];

    // Arguments good enough to reach the tool body, so the anonymous call is refused by RequireUser rather than
    // bouncing off parameter binding first. A tool missing here fails the sweep until someone adds it.
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

            var refused = await IsRefusedAnonymously(name);
            Assert.True(isPublic != refused,
                $"'{name}' is {Tier(isPublic)} but an anonymous call was {(refused ? "refused" : "served")}.");
        }
    }

    [Fact]
    public async Task ToolsList_WithoutAToken_StillOffersTheAccountTools()
    {
        // The whole point of dropping the SDK's list filtering: a model can only tell the user what this site
        // offers if it can see the account tools before they sign in.
        var names = (await ListTools()).Select(tool => tool.Name).ToList();
        Assert.Contains("submit_job_offer", names);
        Assert.Contains("get_my_comments", names);
    }

    [Fact]
    public async Task AnAccountToolCall_WithoutAToken_ExplainsHowToSignIn()
    {
        var response = await CallAnonymously("get_my_comments");
        Assert.Contains("kalandra.tech account", response);
        Assert.Contains("https://www.kalandra.tech/mcp", response);
    }

    private static string Tier(bool isPublic) => isPublic ? "public" : "account-only";

    private async Task<List<(string Name, string Description)>> ListTools()
    {
        var response = await factory.PostMcp("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""");
        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
        return [.. document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => (tool.GetProperty("name").GetString()!, tool.GetProperty("description").GetString() ?? ""))];
    }

    // A refusal is RequireUser's message coming back as a tool error, so the model can read it and prompt the
    // user — not a transport-level failure.
    private async Task<bool> IsRefusedAnonymously(string toolName) =>
        (await CallAnonymously(toolName)).Contains("kalandra.tech account", StringComparison.Ordinal);

    private async Task<string> CallAnonymously(string toolName)
    {
        var arguments = ArgumentsByTool[toolName];
        var request = $$$"""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"{{{toolName}}}","arguments":{{{arguments}}}}}""";
        var response = await factory.PostMcp(request);
        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
        return document.RootElement.ToString();
    }
}
