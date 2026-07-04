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

    // ───── Reactions ─────

    [Fact]
    public async Task GetReactions_UnknownSlug_ReturnsEmptyState()
    {
        SignOut();

        var response = await client.GetAsync($"/api/blog/{NewSlug()}/reactions", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        var counts = json.GetProperty("counts");
        Assert.Equal(0, counts.GetProperty("thumbsUp").GetInt32());
        Assert.Equal(0, counts.GetProperty("thumbsDown").GetInt32());
        Assert.Equal(0, counts.GetProperty("heart").GetInt32());
        Assert.Equal(0, counts.GetProperty("insightful").GetInt32());
        Assert.Equal(0, counts.GetProperty("rocket").GetInt32());
        Assert.Equal(0, json.GetProperty("mine").GetArrayLength());
    }

    [Fact]
    public async Task GetReactions_InvalidSlug_Returns400()
    {
        SignOut();

        var response = await client.GetAsync("/api/blog/Not-A-Valid-Slug/reactions", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertValidationError(await ParseJsonAsync(response), "slug", "InvalidSlug");
    }

    [Fact]
    public async Task ToggleReaction_WithoutAuth_Returns401()
    {
        SignOut();

        var response = await client.PostAsJsonAsync($"/api/blog/{NewSlug()}/reactions/toggle", new { kind = "ThumbsUp" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ToggleReaction_TogglesOnAndOff()
    {
        var slug = NewSlug();
        Authenticate(email: "toggler@test.com");

        var addResponse = await client.PostAsJsonAsync($"/api/blog/{slug}/reactions/toggle", new { kind = "ThumbsUp" }, Ct);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var added = await ParseJsonAsync(addResponse);
        Assert.Equal(1, added.GetProperty("counts").GetProperty("thumbsUp").GetInt32());
        Assert.Equal("ThumbsUp", added.GetProperty("mine")[0].GetString());

        var removeResponse = await client.PostAsJsonAsync($"/api/blog/{slug}/reactions/toggle", new { kind = "ThumbsUp" }, Ct);
        var removed = await ParseJsonAsync(removeResponse);
        Assert.Equal(0, removed.GetProperty("counts").GetProperty("thumbsUp").GetInt32());
        Assert.Equal(0, removed.GetProperty("mine").GetArrayLength());
    }

    [Fact]
    public async Task ToggleReaction_ThumbsDownAndUp_AreIndependent()
    {
        var slug = NewSlug();
        Authenticate(email: "ambivalent@test.com");

        await client.PostAsJsonAsync($"/api/blog/{slug}/reactions/toggle", new { kind = "ThumbsUp" }, Ct);
        var response = await client.PostAsJsonAsync($"/api/blog/{slug}/reactions/toggle", new { kind = "ThumbsDown" }, Ct);

        var json = await ParseJsonAsync(response);
        Assert.Equal(1, json.GetProperty("counts").GetProperty("thumbsUp").GetInt32());
        Assert.Equal(1, json.GetProperty("counts").GetProperty("thumbsDown").GetInt32());
        Assert.Equal(2, json.GetProperty("mine").GetArrayLength());
    }

    [Fact]
    public async Task GetReactions_AggregatesUsersAndOnlyReportsViewersOwn()
    {
        var slug = NewSlug();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        Authenticate(userId: userA, email: "reader-a@test.com");
        await client.PostAsJsonAsync($"/api/blog/{slug}/reactions/toggle", new { kind = "Heart" }, Ct);

        Authenticate(userId: userB, email: "reader-b@test.com");
        await client.PostAsJsonAsync($"/api/blog/{slug}/reactions/toggle", new { kind = "Heart" }, Ct);

        // Anonymous viewer sees aggregate counts but no personal state.
        SignOut();
        var anonymous = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/reactions", Ct));
        Assert.Equal(2, anonymous.GetProperty("counts").GetProperty("heart").GetInt32());
        Assert.Equal(0, anonymous.GetProperty("mine").GetArrayLength());

        Authenticate(userId: userA, email: "reader-a@test.com");
        var mine = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/reactions", Ct));
        Assert.Equal("Heart", mine.GetProperty("mine")[0].GetString());
    }

    [Fact]
    public async Task ToggleReaction_UnknownKind_Returns400()
    {
        Authenticate(email: "unknown-kind@test.com");

        // Unknown enum names fail JSON binding; raw numbers pass binding and are
        // caught by the controller's IsDefined guard.
        var nameResponse = await client.PostAsJsonAsync($"/api/blog/{NewSlug()}/reactions/toggle", new { kind = "Confetti" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, nameResponse.StatusCode);

        var numberResponse = await client.PostAsJsonAsync($"/api/blog/{NewSlug()}/reactions/toggle", new { kind = 99 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, numberResponse.StatusCode);
        AssertValidationError(await ParseJsonAsync(numberResponse), "kind", "UnknownKind");
    }

    // ───── Comments ─────

    [Fact]
    public async Task PostComment_WithoutAuth_Returns401()
    {
        SignOut();

        var response = await client.PostAsJsonAsync($"/api/blog/{NewSlug()}/comments", new { content = "Hello" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostComment_ReturnsFullCommentShape()
    {
        var slug = NewSlug();
        var userId = Guid.NewGuid();
        Authenticate(userId: userId, email: "commenter@test.com");

        var response = await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "First!" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        AssertValidGuid(json, "id");
        Assert.Equal(JsonValueKind.Null, json.GetProperty("parentCommentId").ValueKind);
        Assert.Equal(userId.ToString(), json.GetProperty("userId").GetString());
        // No user_metadata in the test token — display name falls back to the email local part.
        Assert.Equal("commenter", json.GetProperty("authorDisplayName").GetString());
        Assert.Equal("First!", json.GetProperty("content").GetString());
        Assert.False(json.GetProperty("isDeleted").GetBoolean());
        AssertValidTimestamp(json, "postedAt");
    }

    [Fact]
    public async Task Comments_AreReadableAnonymously_AsAThread()
    {
        var slug = NewSlug();
        Authenticate(email: "thread-author@test.com");

        var first = await ParseJsonAsync(await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "Top-level" }, Ct));
        var firstId = first.GetProperty("id").GetString();

        await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "A reply", parentCommentId = firstId }, Ct);

        SignOut();
        var response = await client.GetAsync($"/api/blog/{slug}/comments", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var comments = (await ParseJsonAsync(response)).GetProperty("comments");
        Assert.Equal(2, comments.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, comments[0].GetProperty("parentCommentId").ValueKind);
        Assert.Equal(firstId, comments[1].GetProperty("parentCommentId").GetString());
    }

    [Fact]
    public async Task PostComment_ReplyToMissingParent_Returns400()
    {
        Authenticate(email: "orphan@test.com");

        var response = await client.PostAsJsonAsync(
            $"/api/blog/{NewSlug()}/comments",
            new { content = "A reply", parentCommentId = Guid.NewGuid() },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertValidationError(await ParseJsonAsync(response), "parentCommentId", "ParentCommentNotFound");
    }

    [Fact]
    public async Task PostComment_ReplyToDeletedParent_Returns400()
    {
        var slug = NewSlug();
        Authenticate(email: "necro@test.com");

        var parent = await ParseJsonAsync(await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "Doomed" }, Ct));
        var parentId = parent.GetProperty("id").GetString();
        await client.DeleteAsync($"/api/blog/{slug}/comments/{parentId}", Ct);

        var response = await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "Too late", parentCommentId = parentId }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertValidationError(await ParseJsonAsync(response), "parentCommentId", "ParentCommentDeleted");
    }

    [Fact]
    public async Task PostComment_OverMaxLength_Returns400()
    {
        Authenticate(email: "novelist@test.com");

        var response = await client.PostAsJsonAsync($"/api/blog/{NewSlug()}/comments", new { content = new string('a', 5001) }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertValidationFieldError(await ParseJsonAsync(response), "Content");
    }

    [Fact]
    public async Task PostComment_InvalidSlug_Returns400()
    {
        Authenticate(email: "lost@test.com");

        var response = await client.PostAsJsonAsync("/api/blog/UPPER_case/comments", new { content = "Hello" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertValidationError(await ParseJsonAsync(response), "slug", "InvalidSlug");
    }

    [Fact]
    public async Task DeleteComment_OwnComment_TombstonesIt()
    {
        var slug = NewSlug();
        Authenticate(email: "regretful@test.com");

        var posted = await ParseJsonAsync(await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "Oops" }, Ct));
        var commentId = posted.GetProperty("id").GetString();

        var deleteResponse = await client.DeleteAsync($"/api/blog/{slug}/comments/{commentId}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        SignOut();
        var comments = (await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/comments", Ct))).GetProperty("comments");
        var tombstone = comments[0];
        Assert.True(tombstone.GetProperty("isDeleted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, tombstone.GetProperty("content").ValueKind);
        Assert.Equal(JsonValueKind.Null, tombstone.GetProperty("authorDisplayName").ValueKind);
        Assert.Equal(JsonValueKind.Null, tombstone.GetProperty("userId").ValueKind);
    }

    [Fact]
    public async Task DeleteComment_SomeoneElses_Returns403()
    {
        var slug = NewSlug();
        Authenticate(userId: Guid.NewGuid(), email: "victim@test.com");
        var posted = await ParseJsonAsync(await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "Mine" }, Ct));
        var commentId = posted.GetProperty("id").GetString();

        Authenticate(userId: Guid.NewGuid(), email: "vandal@test.com");
        var response = await client.DeleteAsync($"/api/blog/{slug}/comments/{commentId}", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_AsAdmin_ModeratesAnyComment()
    {
        var slug = NewSlug();
        Authenticate(userId: Guid.NewGuid(), email: "spammer@test.com");
        var posted = await ParseJsonAsync(await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "Spam" }, Ct));
        var commentId = posted.GetProperty("id").GetString();

        Authenticate(userId: Guid.NewGuid(), email: "moderator@test.com", isAdmin: true);
        var response = await client.DeleteAsync($"/api/blog/{slug}/comments/{commentId}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_Missing_Returns404()
    {
        Authenticate(email: "confused@test.com");

        var response = await client.DeleteAsync($"/api/blog/{NewSlug()}/comments/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_Twice_Returns400()
    {
        var slug = NewSlug();
        Authenticate(email: "doubletap@test.com");
        var posted = await ParseJsonAsync(await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "Once" }, Ct));
        var commentId = posted.GetProperty("id").GetString();

        await client.DeleteAsync($"/api/blog/{slug}/comments/{commentId}", Ct);
        var response = await client.DeleteAsync($"/api/blog/{slug}/comments/{commentId}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertValidationError(await ParseJsonAsync(response), "commentId", "AlreadyDeleted");
    }

    // ───── Helpers ─────

    /// <summary>Unique per test — the factory shares one database across the class.</summary>
    private static string NewSlug() => $"post-{Guid.NewGuid():N}";

    private void Authenticate(Guid? userId = null, string email = "test@example.com", bool isAdmin = false)
    {
        var token = JwtTestHelper.GenerateToken(userId, email, isAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void SignOut() => client.DefaultRequestHeaders.Authorization = null;

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(content).RootElement;
    }

    private static void AssertValidGuid(JsonElement json, string property) =>
        Assert.True(Guid.TryParse(json.GetProperty(property).GetString(), out _));

    private static void AssertValidTimestamp(JsonElement json, string property) =>
        Assert.True(DateTimeOffset.TryParse(json.GetProperty(property).GetString(), out _));

    private static void AssertValidationFieldError(JsonElement problem, string field) =>
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));

    private static void AssertValidationError(JsonElement problem, string field, string expectedCode)
    {
        var codes = problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString());
        Assert.Contains(expectedCode, codes);
    }
}
