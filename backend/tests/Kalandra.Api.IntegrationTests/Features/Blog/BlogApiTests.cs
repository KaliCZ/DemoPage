using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kalandra.Api.IntegrationTests.Helpers;

namespace Kalandra.Api.IntegrationTests.Features.Blog;

public class BlogApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ───── Auth gates ─────

    [Fact]
    public async Task AddComment_WithoutAuth_Returns401()
    {
        var slug = NewSlug();
        var response = await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "Hi" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ToggleReaction_WithoutAuth_Returns401()
    {
        var slug = NewSlug();
        var response = await client.PostAsJsonAsync($"/api/blog/{slug}/reactions", new { emoji = "ThumbsUp" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListComments_WithoutAuth_Returns200()
    {
        var slug = NewSlug();
        var response = await client.GetAsync($"/api/blog/{slug}/comments", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Equal(0, json.GetProperty("comments").GetArrayLength());
    }

    [Fact]
    public async Task GetReactions_WithoutAuth_Returns200()
    {
        var slug = NewSlug();
        var response = await client.GetAsync($"/api/blog/{slug}/reactions", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.True(json.GetProperty("counts").GetArrayLength() >= 1);
        Assert.Equal(0, json.GetProperty("userReactions").GetArrayLength());
    }

    // ───── Comments ─────

    [Fact]
    public async Task AddComment_AsSignedInUser_AppearsInList()
    {
        var slug = NewSlug();
        var userId = Authenticate("commenter@test.com");

        var addRes = await client.PostAsJsonAsync(
            $"/api/blog/{slug}/comments",
            new { content = "First!" }, Ct);
        Assert.Equal(HttpStatusCode.OK, addRes.StatusCode);

        var added = await ParseJsonAsync(addRes);
        AssertValidGuid(added, "id");
        Assert.Equal(userId.ToString(), added.GetProperty("userId").GetString());
        Assert.Equal("commenter@test.com", added.GetProperty("userEmail").GetString());
        Assert.Equal("First!", added.GetProperty("content").GetString());
        AssertValidTimestamp(added, "createdAt");

        // Anonymous reader can read
        ClearAuth();
        var listRes = await client.GetAsync($"/api/blog/{slug}/comments", Ct);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await ParseJsonAsync(listRes);
        Assert.Equal(1, list.GetProperty("comments").GetArrayLength());
        Assert.Equal("First!", list.GetProperty("comments")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task AddComment_TrimsAndRejectsWhitespaceOnly()
    {
        var slug = NewSlug();
        Authenticate();

        var blankRes = await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "   " }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blankRes.StatusCode);
        var blank = await ParseJsonAsync(blankRes);
        AssertValidationError(blank, "content", "ContentRequired");

        var goodRes = await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "  hi  " }, Ct);
        Assert.Equal(HttpStatusCode.OK, goodRes.StatusCode);
        var good = await ParseJsonAsync(goodRes);
        Assert.Equal("hi", good.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ListComments_OnlyContainsCommentsForRequestedSlug()
    {
        var slugA = NewSlug();
        var slugB = NewSlug();

        Authenticate("a@test.com");
        await client.PostAsJsonAsync($"/api/blog/{slugA}/comments", new { content = "from A" }, Ct);
        await client.PostAsJsonAsync($"/api/blog/{slugB}/comments", new { content = "from B" }, Ct);

        var aList = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slugA}/comments", Ct));
        var bList = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slugB}/comments", Ct));

        Assert.Equal(1, aList.GetProperty("comments").GetArrayLength());
        Assert.Equal("from A", aList.GetProperty("comments")[0].GetProperty("content").GetString());
        Assert.Equal(1, bList.GetProperty("comments").GetArrayLength());
        Assert.Equal("from B", bList.GetProperty("comments")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task AddComment_InvalidSlug_Returns400()
    {
        Authenticate();
        var response = await client.PostAsJsonAsync(
            "/api/blog/Invalid_SLUG!/comments",
            new { content = "Hi" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ParseJsonAsync(response);
        AssertValidationError(json, "slug", "InvalidSlug");
    }

    // ───── Reactions ─────

    [Fact]
    public async Task ToggleReaction_AddsThenRemoves()
    {
        var slug = NewSlug();
        var userId = Authenticate("react@test.com");

        var addedRes = await client.PostAsJsonAsync(
            $"/api/blog/{slug}/reactions",
            new { emoji = "ThumbsUp" }, Ct);
        Assert.Equal(HttpStatusCode.OK, addedRes.StatusCode);
        Assert.Equal("Added", (await ParseJsonAsync(addedRes)).GetProperty("action").GetString());

        // Counts now show 1
        var afterAdd = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/reactions", Ct));
        AssertEmojiCount(afterAdd, "ThumbsUp", 1);
        Assert.Contains(
            afterAdd.GetProperty("userReactions").EnumerateArray(),
            e => e.GetString() == "ThumbsUp");

        // Toggle off
        var removedRes = await client.PostAsJsonAsync(
            $"/api/blog/{slug}/reactions",
            new { emoji = "ThumbsUp" }, Ct);
        Assert.Equal(HttpStatusCode.OK, removedRes.StatusCode);
        Assert.Equal("Removed", (await ParseJsonAsync(removedRes)).GetProperty("action").GetString());

        var afterRemove = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/reactions", Ct));
        AssertEmojiCount(afterRemove, "ThumbsUp", 0);
        Assert.Equal(0, afterRemove.GetProperty("userReactions").GetArrayLength());
    }

    [Fact]
    public async Task ToggleReaction_DifferentUsersAreTrackedSeparately()
    {
        var slug = NewSlug();

        Authenticate("alice@test.com");
        await client.PostAsJsonAsync($"/api/blog/{slug}/reactions", new { emoji = "Heart" }, Ct);

        Authenticate("bob@test.com");
        await client.PostAsJsonAsync($"/api/blog/{slug}/reactions", new { emoji = "Heart" }, Ct);

        // Bob still authenticated — sees self in userReactions
        var bobView = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/reactions", Ct));
        AssertEmojiCount(bobView, "Heart", 2);
        Assert.Single(bobView.GetProperty("userReactions").EnumerateArray());
    }

    [Fact]
    public async Task GetReactions_AnonymousViewerSeesNoUserReactions()
    {
        var slug = NewSlug();
        Authenticate("solo@test.com");
        await client.PostAsJsonAsync($"/api/blog/{slug}/reactions", new { emoji = "Rocket" }, Ct);

        ClearAuth();
        var view = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/reactions", Ct));
        AssertEmojiCount(view, "Rocket", 1);
        Assert.Equal(0, view.GetProperty("userReactions").GetArrayLength());
    }

    [Fact]
    public async Task GetReactions_ReturnsAllSupportedEmoji()
    {
        var slug = NewSlug();
        var view = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/reactions", Ct));

        // Stable wire contract: every supported emoji appears at zero, in declaration order.
        var counts = view.GetProperty("counts");
        var emojis = counts.EnumerateArray().Select(e => e.GetProperty("emoji").GetString()).ToArray();
        Assert.Equal(new[] { "ThumbsUp", "Heart", "Rocket", "Eyes", "Tada", "Laugh" }, emojis);
    }

    // ───── Helpers ─────

    private static string NewSlug() => $"post-{Guid.NewGuid():n}";

    private Guid Authenticate(string email = "test@example.com", bool isAdmin = false)
    {
        var userId = Guid.NewGuid();
        var token = JwtTestHelper.GenerateToken(userId, email, isAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return userId;
    }

    private void ClearAuth() => client.DefaultRequestHeaders.Authorization = null;

    private static string AssertValidGuid(JsonElement parent, string propertyName)
    {
        var raw = parent.GetProperty(propertyName).GetString();
        Assert.NotNull(raw);
        Assert.NotEqual(Guid.Empty, Guid.Parse(raw));
        return raw;
    }

    private static void AssertValidTimestamp(JsonElement parent, string propertyName)
    {
        var raw = parent.GetProperty(propertyName).GetString();
        Assert.NotNull(raw);
        Assert.True(
            DateTimeOffset.TryParse(raw, out var parsed) && parsed > DateTimeOffset.UnixEpoch,
            $"Expected valid timestamp for '{propertyName}', got '{raw}'");
    }

    private static void AssertValidationError(JsonElement json, string field, string expectedCode)
    {
        var errors = json.GetProperty("errors").GetProperty(field);
        Assert.Contains(
            errors.EnumerateArray(),
            e => e.GetString() == expectedCode);
    }

    private static void AssertEmojiCount(JsonElement view, string emoji, int expectedCount)
    {
        var entry = view.GetProperty("counts")
            .EnumerateArray()
            .First(e => e.GetProperty("emoji").GetString() == emoji);
        Assert.Equal(expectedCount, entry.GetProperty("count").GetInt32());
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(), cancellationToken: CancellationToken.None);
        return doc.RootElement;
    }
}
