using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kalandra.Api.IntegrationTests.Helpers;

namespace Kalandra.Api.IntegrationTests.Features.Me;

public class MeCommentsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetMyComments_WithoutAuth_Returns401()
    {
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("/api/me/comments", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyComments_ReturnsBlogCommentWithRepliesOnly()
    {
        var slug = NewSlug();
        var authorId = Guid.NewGuid();

        Authenticate(authorId, "me-blog-author@test.com");
        var mine = await ParseJsonAsync(await client.PostAsJsonAsync(
            $"/api/blog/{slug}/comments", new { content = "My insightful comment" }, Ct));
        var myCommentId = mine.GetProperty("id").GetString();

        // A reply to my comment and an unrelated top-level comment by someone else.
        Authenticate(Guid.NewGuid(), "me-blog-replier@test.com");
        await client.PostAsJsonAsync(
            $"/api/blog/{slug}/comments", new { content = "Replying to you", parentCommentId = myCommentId }, Ct);
        await client.PostAsJsonAsync(
            $"/api/blog/{slug}/comments", new { content = "Unrelated remark" }, Ct);

        Authenticate(authorId, "me-blog-author@test.com");
        var json = await ParseJsonAsync(await client.GetAsync("/api/me/comments", Ct));

        var entry = FindBlogEntry(json, slug);
        Assert.Equal("My insightful comment", entry.GetProperty("comment").GetProperty("content").GetString());
        Assert.Equal(1, entry.GetProperty("replies").GetArrayLength());
        Assert.Equal("Replying to you", entry.GetProperty("replies")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GetMyComments_ExcludesMyDeletedBlogComments()
    {
        var slug = NewSlug();
        var authorId = Guid.NewGuid();

        Authenticate(authorId, "me-blog-deleter@test.com");
        var posted = await ParseJsonAsync(await client.PostAsJsonAsync(
            $"/api/blog/{slug}/comments", new { content = "Soon deleted" }, Ct));
        var commentId = posted.GetProperty("id").GetString();
        var delete = await client.DeleteAsync($"/api/blog/{slug}/comments/{commentId}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var json = await ParseJsonAsync(await client.GetAsync("/api/me/comments", Ct));

        Assert.DoesNotContain(
            json.GetProperty("blogComments").EnumerateArray(),
            e => e.GetProperty("slug").GetString() == slug);
    }

    [Fact]
    public async Task GetMyComments_PairsJobOfferRepliesWithMyComments()
    {
        var (offerId, ownerId) = await CreateOfferAs("me-offer-owner@test.com");

        await PostOfferComment(offerId, "Any update on my offer?");

        // The site owner (admin) responds on the owner's offer.
        Authenticate(Guid.NewGuid(), "me-offer-admin@test.com", isAdmin: true);
        await PostOfferComment(offerId, "Looking at it this week.");

        Authenticate(ownerId, "me-offer-owner@test.com");
        var json = await ParseJsonAsync(await client.GetAsync("/api/me/comments", Ct));

        var entry = FindJobOfferEntry(json, offerId);
        Assert.Equal("Acme Corp", entry.GetProperty("companyName").GetString());
        Assert.Equal("Senior Developer", entry.GetProperty("jobTitle").GetString());
        Assert.Equal("Any update on my offer?", entry.GetProperty("comment").GetProperty("content").GetString());
        Assert.Equal(1, entry.GetProperty("replies").GetArrayLength());
        Assert.Equal("Looking at it this week.", entry.GetProperty("replies")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GetMyComments_AdminSeesOwnCommentsOnForeignOffers()
    {
        var (offerId, ownerId) = await CreateOfferAs("me-admin-offer-owner@test.com");
        await PostOfferComment(offerId, "Owner opens the thread");

        var adminId = Guid.NewGuid();
        Authenticate(adminId, "me-admin@test.com", isAdmin: true);
        await PostOfferComment(offerId, "Admin answers");

        Authenticate(ownerId, "me-admin-offer-owner@test.com");
        await PostOfferComment(offerId, "Owner follows up");

        Authenticate(adminId, "me-admin@test.com", isAdmin: true);
        var json = await ParseJsonAsync(await client.GetAsync("/api/me/comments", Ct));

        var entry = FindJobOfferEntry(json, offerId);
        Assert.Equal("Admin answers", entry.GetProperty("comment").GetProperty("content").GetString());
        Assert.Equal(1, entry.GetProperty("replies").GetArrayLength());
        Assert.Equal("Owner follows up", entry.GetProperty("replies")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GetMyComments_WithNoActivity_ReturnsEmptyLists()
    {
        Authenticate(Guid.NewGuid(), "me-lurker@test.com");

        var json = await ParseJsonAsync(await client.GetAsync("/api/me/comments", Ct));

        Assert.Equal(0, json.GetProperty("blogComments").GetArrayLength());
        Assert.Equal(0, json.GetProperty("jobOfferComments").GetArrayLength());
    }

    // ───── Helpers ─────

    private static string NewSlug() => $"post-{Guid.NewGuid():N}";

    private void Authenticate(Guid userId, string email, bool isAdmin = false)
    {
        var token = JwtTestHelper.GenerateToken(userId, email, isAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<(string OfferId, Guid OwnerId)> CreateOfferAs(string email)
    {
        var ownerId = Guid.NewGuid();
        Authenticate(ownerId, email);

        var form = new MultipartFormDataContent
        {
            { new StringContent("test-token"), "cf-turnstile-response" },
            { new StringContent("Acme Corp"), "CompanyName" },
            { new StringContent("John Doe"), "ContactName" },
            { new StringContent("john@acme.com"), "ContactEmail" },
            { new StringContent("Senior Developer"), "JobTitle" },
            { new StringContent("A role description."), "Description" },
            { new StringContent("true"), "IsRemote" },
        };

        var response = await client.PostAsync("/api/job-offers", form, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await ParseJsonAsync(response);
        return (json.GetProperty("id").GetString()!, ownerId);
    }

    private async Task PostOfferComment(string offerId, string content)
    {
        var response = await client.PostAsJsonAsync($"/api/job-offers/{offerId}/comments", new { content }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static JsonElement FindBlogEntry(JsonElement myComments, string slug)
    {
        var match = myComments.GetProperty("blogComments").EnumerateArray()
            .Where(e => e.GetProperty("slug").GetString() == slug)
            .ToList();
        return Assert.Single(match);
    }

    private static JsonElement FindJobOfferEntry(JsonElement myComments, string offerId)
    {
        var match = myComments.GetProperty("jobOfferComments").EnumerateArray()
            .Where(e => e.GetProperty("jobOfferId").GetString() == offerId)
            .ToList();
        return Assert.Single(match);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(content).RootElement;
    }
}
