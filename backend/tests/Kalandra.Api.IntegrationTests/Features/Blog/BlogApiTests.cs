using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kalandra.Api.IntegrationTests.Helpers;
using Kalandra.Blog.Entities;
using Kalandra.Infrastructure.Users;

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
    public async Task GetReactions_InvalidSlug_Returns404()
    {
        SignOut();

        var response = await client.GetAsync("/api/blog/Not-A-Valid-Slug/reactions", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
    public async Task ToggleReaction_EveryKindRoundTripsOnTheWire()
    {
        var slug = NewSlug();
        Authenticate(email: "completionist@test.com");

        // Iterating the domain enum makes a sixth kind that's missing from the
        // hardcoded counts response fail here instead of shipping as a silent zero.
        foreach (var kind in Enum.GetValues<BlogReactionKind>())
        {
            var response = await client.PostAsJsonAsync($"/api/blog/{slug}/reactions/toggle", new { kind = kind.ToString() }, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var json = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/reactions", Ct));
        var counts = json.GetProperty("counts");
        foreach (var kind in Enum.GetValues<BlogReactionKind>())
        {
            var property = char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
            Assert.True(counts.TryGetProperty(property, out var count), $"counts is missing '{property}'");
            Assert.Equal(1, count.GetInt32());
        }
        Assert.Equal(Enum.GetValues<BlogReactionKind>().Length, json.GetProperty("mine").GetArrayLength());
    }

    [Fact]
    public async Task ReactionsAndComments_AreIsolatedPerSlug()
    {
        var slugA = NewSlug();
        var slugB = NewSlug();
        Authenticate(email: "isolated@test.com");

        await client.PostAsJsonAsync($"/api/blog/{slugA}/reactions/toggle", new { kind = "Heart" }, Ct);
        await client.PostAsJsonAsync($"/api/blog/{slugA}/comments", new { content = "Only on A" }, Ct);

        var reactions = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slugB}/reactions", Ct));
        Assert.Equal(0, reactions.GetProperty("counts").GetProperty("heart").GetInt32());
        Assert.Equal(0, reactions.GetProperty("mine").GetArrayLength());

        var comments = await ParseJsonAsync(await client.GetAsync($"/api/blog/{slugB}/comments", Ct));
        Assert.Equal(0, comments.GetProperty("comments").GetArrayLength());
    }

    [Fact]
    public async Task ToggleReaction_UnknownKind_Returns400()
    {
        Authenticate(email: "unknown-kind@test.com");

        // Both an unknown enum name and a raw number fail JSON binding (the converter
        // runs with allowIntegerValues: false), so each is a 400 before the action runs.
        var nameResponse = await client.PostAsJsonAsync($"/api/blog/{NewSlug()}/reactions/toggle", new { kind = "Confetti" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, nameResponse.StatusCode);

        var numberResponse = await client.PostAsJsonAsync($"/api/blog/{NewSlug()}/reactions/toggle", new { kind = 99 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, numberResponse.StatusCode);
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
    public async Task Comments_ReflectAuthorsCurrentProfile_NotThePostTimeSnapshot()
    {
        var slug = NewSlug();
        var userId = Guid.NewGuid();
        Authenticate(userId: userId, email: "chameleon@test.com");

        // No user_metadata in the token, so the snapshot name is the email local part with no avatar.
        await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "Look at my avatar" }, Ct);

        // The author later sets a display name and avatar; every past comment must show them.
        factory.UserInfoService.Profiles[userId] = new UserPublicInfo("Chameleon Prime", new Uri("https://cdn.test/avatar.png"));

        SignOut();
        var comments = (await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/comments", Ct))).GetProperty("comments");
        var comment = comments[0];
        Assert.Equal("Chameleon Prime", comment.GetProperty("authorDisplayName").GetString());
        Assert.Equal("https://cdn.test/avatar.png", comment.GetProperty("authorAvatarUrl").GetString());
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

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task PostComment_MissingContent_Returns400(string? content)
    {
        Authenticate(email: "spacebar@test.com");

        var response = await client.PostAsJsonAsync($"/api/blog/{NewSlug()}/comments", new { content }, Ct);

        // NonEmptyString rejects null/empty at binding — a plain 400, no stable error code.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostComment_PreservesContentWhitespace()
    {
        var slug = NewSlug();
        Authenticate(email: "spacer@test.com");

        // Content is stored verbatim — no server-side trimming or whitespace collapsing.
        var response = await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "spaced   out   comment" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ParseJsonAsync(response);
        Assert.Equal("spaced   out   comment", json.GetProperty("content").GetString());
    }

    [Fact]
    public async Task PostComment_InvalidSlug_Returns404()
    {
        Authenticate(email: "lost@test.com");

        var response = await client.PostAsJsonAsync("/api/blog/UPPER_case/comments", new { content = "Hello" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    // ───── Notification emails (via the Temporal workflow) ─────

    [Fact]
    public async Task PostComment_NotifiesTheBlogAuthor()
    {
        var slug = NewSlug();
        var content = $"Author should hear about this {Guid.NewGuid():N}";
        Authenticate(email: "notifier@test.com");

        var response = await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var email = Assert.Single(await WaitForEmailsAsync(m => m.TextBody.Value.Contains(content), expectedCount: 1));
        Assert.Equal("author@kalandra.local", email.To.Address);
        Assert.Contains(slug, email.Subject.Value);
        Assert.Contains($"https://www.kalandra.tech/blog/{slug}", email.TextBody.Value);
        Assert.Contains("notifier", email.TextBody.Value);
    }

    [Fact]
    public async Task Reply_NotifiesBlogAuthorAndParentCommentAuthor()
    {
        var slug = NewSlug();
        Authenticate(userId: Guid.NewGuid(), email: "parent-author@test.com");
        var parent = await ParseJsonAsync(await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "Original thought" }, Ct));
        var parentId = parent.GetProperty("id").GetString();

        var replyContent = $"Strong disagreement {Guid.NewGuid():N}";
        Authenticate(userId: Guid.NewGuid(), email: "replier@test.com");
        await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = replyContent, parentCommentId = parentId }, Ct);

        var emails = await WaitForEmailsAsync(m => m.TextBody.Value.Contains(replyContent), expectedCount: 2);
        Assert.Equal(2, emails.Length);
        Assert.Contains(emails, m => m.To.Address == "author@kalandra.local");
        var parentNotification = Assert.Single(emails, m => m.To.Address == "parent-author@test.com");
        Assert.StartsWith("New reply to your comment", parentNotification.Subject.Value);
    }

    [Fact]
    public async Task ReplyToYourOwnComment_OnlyNotifiesTheBlogAuthor()
    {
        var slug = NewSlug();
        var userId = Guid.NewGuid();
        Authenticate(userId: userId, email: "monologuist@test.com");
        var parent = await ParseJsonAsync(await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content = "First thought" }, Ct));

        var replyContent = $"Second thought {Guid.NewGuid():N}";
        await client.PostAsJsonAsync(
            $"/api/blog/{slug}/comments",
            new { content = replyContent, parentCommentId = parent.GetProperty("id").GetString() },
            Ct);

        var emails = await WaitForEmailsAsync(m => m.TextBody.Value.Contains(replyContent), expectedCount: 1);
        // Grace period: a wrong extra notification would arrive moments later.
        await Task.Delay(1500, Ct);
        emails = [.. factory.EmailSender.Sent.Where(m => m.TextBody.Value.Contains(replyContent))];

        var email = Assert.Single(emails);
        Assert.Equal("author@kalandra.local", email.To.Address);
    }

    [Fact]
    public async Task AuthorsOwnComment_SendsNoEmailAtAll()
    {
        var slug = NewSlug();
        var content = $"Author talking to themselves {Guid.NewGuid():N}";
        Authenticate(email: "author@kalandra.local");

        var response = await client.PostAsJsonAsync($"/api/blog/{slug}/comments", new { content }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The comment must still be stored even though nobody is notified.
        SignOut();
        var comments = (await ParseJsonAsync(await client.GetAsync($"/api/blog/{slug}/comments", Ct))).GetProperty("comments");
        Assert.Equal(1, comments.GetArrayLength());

        await Task.Delay(1500, Ct);
        Assert.DoesNotContain(factory.EmailSender.Sent, m => m.TextBody.Value.Contains(content));
    }

    // ───── Helpers ─────

    private async Task<Kalandra.Infrastructure.Email.EmailMessage[]> WaitForEmailsAsync(
        Func<Kalandra.Infrastructure.Email.EmailMessage, bool> predicate, int expectedCount)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var matches = factory.EmailSender.Sent.Where(predicate).ToArray();
            if (matches.Length >= expectedCount)
                return matches;
            await Task.Delay(200, Ct);
        }
        return [.. factory.EmailSender.Sent.Where(predicate)];
    }

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
