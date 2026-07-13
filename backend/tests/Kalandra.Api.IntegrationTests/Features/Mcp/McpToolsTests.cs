using Kalandra.Api.Features.Mcp;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Api.IntegrationTests.Helpers;
using Kalandra.Blog.Commands;
using Kalandra.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;

namespace Kalandra.Api.IntegrationTests.Features.Mcp;

/// <summary>
/// Exercises the MCP tools directly against the real domain handlers and database, with a chosen
/// caller. This covers the tool-specific logic — auth guard, command building, error translation —
/// while the handlers themselves are covered by the controller tests they share. (The MCP protocol
/// transport itself is standard SDK wiring, verified by an end-to-end smoke test, not here: its
/// streamable-HTTP client can't run over the in-memory test server.)
/// </summary>
public class McpToolsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CurrentUser Owner = new(
        Guid.NewGuid(), new System.Net.Mail.MailAddress("mcp-owner@test.com"), "MCP Owner".ToNonEmpty(), []);

    [Fact]
    public async Task ListBlogPosts_WithoutAuth_ReturnsTheFeed_WithNoReadStatus()
    {
        await using var scope = NewScope();
        var tools = Blog(scope);

        var post = (await tools.ListBlogPosts(Ct))[0];

        Assert.Equal(TestWebApplicationFactory.BlogFeedSlug, post.Slug);
        // Anonymous callers have no reading history to report.
        Assert.Null(post.Watched);
        Assert.Null(post.ViewerViews);
    }

    [Fact]
    public async Task ListBlogPosts_SignedIn_FlagsPostsTheUserHasViewed()
    {
        var reader = new CurrentUser(
            Guid.NewGuid(), new System.Net.Mail.MailAddress("mcp-reader@test.com"), "Reader".ToNonEmpty(), []);

        await using (var seed = NewScope())
        {
            var recordView = seed.ServiceProvider.GetRequiredService<RecordBlogPostViewHandler>();
            await recordView.RecordAndSave(
                new RecordBlogPostViewCommand(
                    TestWebApplicationFactory.BlogFeedSlug, Guid.NewGuid(), reader.Id, DateTimeOffset.UtcNow),
                Ct);
        }

        var post = Assert.Single(
            await WithTools(reader, tools => tools.Blog.ListBlogPosts(Ct)),
            p => p.Slug == TestWebApplicationFactory.BlogFeedSlug);

        Assert.True(post.Watched);
        Assert.Equal(1, post.ViewerViews);
    }

    [Fact]
    public async Task SubmitJobOffer_WithoutUser_ThrowsWithAuthGuidance()
    {
        await using var scope = NewScope();
        var tools = JobOffer(scope);

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            tools.SubmitJobOffer("Acme", "Jane", "jane@acme.com", "Staff Engineer", "A role.", isRemote: true, ct: Ct));

        Assert.Contains("Authorization", ex.Message);
    }

    [Fact]
    public async Task SubmitJobOffer_AsUser_Stores_AndSurfacesInListAndMyComments()
    {
        var submitted = await WithTools(Owner, tools =>
            tools.JobOffer.SubmitJobOffer("Acme Corp", "Jane Doe", "jane@acme.com", "Staff Engineer",
                "Build cool things.", isRemote: true, location: "Remote", ct: Ct));

        Assert.Equal("Acme Corp", submitted.CompanyName.Value);

        var mine = await WithTools(Owner, tools => tools.JobOffer.ListMyJobOffers(Ct));
        Assert.Contains(mine, o => o.Id == submitted.Id);

        await WithTools(Owner, tools => tools.JobOffer.AddJobOfferComment(submitted.Id, "Any update?", Ct));

        var comments = await WithTools(Owner, tools => tools.Blog.GetMyComments(Ct));
        var entry = Assert.Single(comments.JobOfferComments, e => e.JobOfferId == submitted.Id);
        Assert.Equal("Any update?", entry.Comment.Content.Value);
    }

    [Fact]
    public async Task AddJobOfferComment_OnSomeoneElsesOffer_Throws()
    {
        var offer = await WithTools(Owner, tools =>
            tools.JobOffer.SubmitJobOffer("Acme", "Jane", "jane@acme.com", "Dev", "Role.", isRemote: false, ct: Ct));

        var stranger = new CurrentUser(
            Guid.NewGuid(), new System.Net.Mail.MailAddress("stranger@test.com"), "Stranger".ToNonEmpty(), []);

        var ex = await WithTools(stranger, tools =>
            Assert.ThrowsAsync<McpException>(() => tools.JobOffer.AddJobOfferComment(offer.Id, "hi", Ct)));

        Assert.Contains("doesn't belong to you", ex.Message);
    }

    [Fact]
    public async Task PostBlogComment_AsUser_IsReadableViaGetComments()
    {
        var slug = $"post-{Guid.NewGuid():N}";

        var posted = await WithTools(Owner, tools => tools.Blog.PostBlogComment(slug, "First!", ct: Ct));
        Assert.Equal("First!", posted.Content!.Value);

        var thread = await WithTools(Owner, tools => tools.Blog.GetBlogPostComments(slug, Ct));
        Assert.Contains(thread.Comments, c => c.Content?.Value == "First!");
    }

    [Fact]
    public async Task GetMyComments_IncludesTheUsersBlogComment()
    {
        var slug = $"post-{Guid.NewGuid():N}";
        await WithTools(Owner, tools => tools.Blog.PostBlogComment(slug, "My take on this", ct: Ct));

        var mine = await WithTools(Owner, tools => tools.Blog.GetMyComments(Ct));

        var entry = Assert.Single(mine.BlogComments, e => e.Slug == slug);
        Assert.Equal("My take on this", entry.Comment.Content!.Value);
    }

    [Fact]
    public async Task PostBlogComment_WithoutUser_ThrowsWithAuthGuidance()
    {
        await using var scope = NewScope();
        var tools = Blog(scope);

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.PostBlogComment("some-post", "hi", ct: Ct));

        Assert.Contains("Authorization", ex.Message);
    }

    // ───── Helpers ─────

    // A fresh DI scope per call mirrors the per-request scoping the MCP endpoint gives each tool call.
    private AsyncServiceScope NewScope() => factory.Services.CreateAsyncScope();

    private static JobOfferMcpTools JobOffer(AsyncServiceScope scope, CurrentUser? user = null) =>
        ActivatorUtilities.CreateInstance<JobOfferMcpTools>(scope.ServiceProvider, new FakeCurrentUser(user));

    private static BlogMcpTools Blog(AsyncServiceScope scope, CurrentUser? user = null) =>
        ActivatorUtilities.CreateInstance<BlogMcpTools>(scope.ServiceProvider, new FakeCurrentUser(user));

    private async Task<T> WithTools<T>(CurrentUser user, Func<(JobOfferMcpTools JobOffer, BlogMcpTools Blog), Task<T>> body)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await body((JobOffer(scope, user), Blog(scope, user)));
    }

    private sealed class FakeCurrentUser(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? User => user;
        public CurrentUser RequiredUser => user ?? throw new InvalidOperationException("No user.");
    }
}
