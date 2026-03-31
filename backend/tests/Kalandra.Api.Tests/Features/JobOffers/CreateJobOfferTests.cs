using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kalandra.Api.Features.JobOffers.Comments;
using Kalandra.Api.Features.JobOffers.Create;
using Kalandra.Api.Features.JobOffers.GetDetail;
using Kalandra.Api.Features.JobOffers.History;
using Kalandra.Api.Features.JobOffers.List;
using Kalandra.Api.Tests.Helpers;

namespace Kalandra.Api.Tests.Features.JobOffers;

public class CreateJobOfferTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_WithoutAuth_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAuth_Returns201()
    {
        var token = JwtTestHelper.GenerateToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateJobOfferResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Create_WithInvalidData_Returns400()
    {
        var token = JwtTestHelper.GenerateToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new CreateJobOfferRequest("", "", "", "", "", null, null, false, null, null);
        var response = await _client.PostAsJsonAsync("/api/job-offers", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListAll_AsNonAdmin_Returns403()
    {
        var token = JwtTestHelper.GenerateToken("regular-user");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/job-offers");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListAll_AsAdmin_Returns200()
    {
        var token = JwtTestHelper.GenerateToken("admin-user-id", "admin@test.com", isAdmin: true);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/job-offers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_SubmitCancelAndVerifyHistory()
    {
        var token = JwtTestHelper.GenerateToken("lifecycle-user", "lifecycle@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Submit
        var createResponse = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateJobOfferResponse>();
        var id = created!.Id;

        // List mine
        var listResponse = await _client.GetAsync("/api/job-offers/mine");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<ListJobOffersResponse>();
        Assert.Contains(list!.Items, i => i.Id == id);

        // Get detail
        var detailResponse = await _client.GetAsync($"/api/job-offers/{id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        // Get history
        var historyResponse = await _client.GetAsync($"/api/job-offers/{id}/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<JobOfferHistoryResponse>();
        Assert.Single(history!.Entries);
        Assert.Equal("Submitted", history.Entries[0].EventType);

        // Cancel
        var cancelResponse = await _client.PostAsJsonAsync($"/api/job-offers/{id}/cancel",
            new { reason = "Made a mistake" });
        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

        // Verify history after cancel
        var historyAfter = await _client.GetAsync($"/api/job-offers/{id}/history");
        var historyData = await historyAfter.Content.ReadFromJsonAsync<JobOfferHistoryResponse>();
        Assert.Equal(2, historyData!.Entries.Count);
        Assert.Equal("Cancelled", historyData.Entries[1].EventType);
    }

    [Fact]
    public async Task AdminCanChangeStatus_AndHistoryRecords()
    {
        // Create as regular user
        var userToken = JwtTestHelper.GenerateToken("status-user", "status@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var createResponse = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<CreateJobOfferResponse>();
        var id = created!.Id;

        // Change status as admin
        var adminToken = JwtTestHelper.GenerateToken("admin-user-id", "admin@test.com", isAdmin: true);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var statusResponse = await _client.PatchAsJsonAsync($"/api/job-offers/{id}/status",
            new { status = 1, adminNotes = "Looks promising" }); // InReview = 1
        Assert.Equal(HttpStatusCode.NoContent, statusResponse.StatusCode);

        // Verify history
        var historyResponse = await _client.GetAsync($"/api/job-offers/{id}/history");
        var history = await historyResponse.Content.ReadFromJsonAsync<JobOfferHistoryResponse>();
        Assert.Equal(2, history!.Entries.Count);
        Assert.Equal("StatusChanged", history.Entries[1].EventType);
    }

    [Fact]
    public async Task CannotCancel_OtherUsersOffer()
    {
        // Create as user1
        var token1 = JwtTestHelper.GenerateToken("owner-user", "owner@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var createResponse = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<CreateJobOfferResponse>();

        // Try to cancel as user2
        var token2 = JwtTestHelper.GenerateToken("other-user", "other@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var cancelResponse = await _client.PostAsJsonAsync($"/api/job-offers/{created!.Id}/cancel",
            new { reason = "Trying to cancel someone else's offer" });
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task CannotView_OtherUsersOffer()
    {
        // Create as user1
        var token1 = JwtTestHelper.GenerateToken("view-owner", "viewowner@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var createRes = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>();

        // Try to view as user2
        var token2 = JwtTestHelper.GenerateToken("view-other", "viewother@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var detailRes = await _client.GetAsync($"/api/job-offers/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, detailRes.StatusCode);

        var historyRes = await _client.GetAsync($"/api/job-offers/{created.Id}/history");
        Assert.Equal(HttpStatusCode.NotFound, historyRes.StatusCode);

        var commentsRes = await _client.GetAsync($"/api/job-offers/{created.Id}/comments");
        Assert.Equal(HttpStatusCode.NotFound, commentsRes.StatusCode);
    }

    [Fact]
    public async Task CannotEdit_OtherUsersOffer()
    {
        // Create as user1
        var token1 = JwtTestHelper.GenerateToken("edit-owner", "editowner@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var createRes = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>();

        // Try to edit as user2
        var token2 = JwtTestHelper.GenerateToken("edit-other", "editother@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var editRes = await _client.PutAsJsonAsync($"/api/job-offers/{created!.Id}", CreateValidRequest());
        Assert.Equal(HttpStatusCode.Forbidden, editRes.StatusCode);
    }

    [Fact]
    public async Task ListMine_DoesNotShowOtherUsersOffers()
    {
        // Create as user1
        var token1 = JwtTestHelper.GenerateToken("list-owner", "listowner@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var createRes = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>();

        // List as user2
        var token2 = JwtTestHelper.GenerateToken("list-other", "listother@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var listRes = await _client.GetAsync("/api/job-offers/mine");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await listRes.Content.ReadFromJsonAsync<ListJobOffersResponse>();
        Assert.DoesNotContain(list!.Items, i => i.Id == created!.Id);
    }

    [Fact]
    public async Task Edit_WhenSubmitted_Succeeds()
    {
        var token = JwtTestHelper.GenerateToken("edit-user", "edit@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create
        var createRes = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>();

        // Edit
        var editRequest = new CreateJobOfferRequest(
            "Updated Corp", "Jane Doe", "jane@updated.com", "CTO",
            "Updated description.", "$200k", "Remote", true, null, null);
        var editRes = await _client.PutAsJsonAsync($"/api/job-offers/{created!.Id}", editRequest);
        Assert.Equal(HttpStatusCode.NoContent, editRes.StatusCode);

        // Verify detail reflects edit
        var detail = await _client.GetFromJsonAsync<GetJobOfferDetailResponse>($"/api/job-offers/{created.Id}");
        Assert.Equal("Updated Corp", detail!.CompanyName);
        Assert.Equal("CTO", detail.JobTitle);

        // Verify history has Edited event
        var history = await _client.GetFromJsonAsync<JobOfferHistoryResponse>($"/api/job-offers/{created.Id}/history");
        Assert.Equal(2, history!.Entries.Count);
        Assert.Equal("Edited", history.Entries[1].EventType);
    }

    [Fact]
    public async Task Edit_WhenNotSubmitted_Fails()
    {
        // Create and cancel
        var token = JwtTestHelper.GenerateToken("edit-fail-user", "editfail@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRes = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>();

        await _client.PostAsJsonAsync($"/api/job-offers/{created!.Id}/cancel", new { reason = "" });

        // Try to edit cancelled offer
        var editRes = await _client.PutAsJsonAsync($"/api/job-offers/{created.Id}", CreateValidRequest());
        Assert.Equal(HttpStatusCode.BadRequest, editRes.StatusCode);
    }

    [Fact]
    public async Task Comments_OwnerCanAddAndList()
    {
        var token = JwtTestHelper.GenerateToken("comment-user", "comment@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRes = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>();

        // Add comment
        var commentRes = await _client.PostAsJsonAsync(
            $"/api/job-offers/{created!.Id}/comments", new { content = "Hello, any update?" });
        Assert.Equal(HttpStatusCode.NoContent, commentRes.StatusCode);

        // List comments
        var listRes = await _client.GetAsync($"/api/job-offers/{created.Id}/comments");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var comments = await listRes.Content.ReadFromJsonAsync<ListCommentsResponse>();
        Assert.Single(comments!.Comments);
        Assert.Equal("Hello, any update?", comments.Comments[0].Content);
    }

    [Fact]
    public async Task Comments_AdminCanReply()
    {
        // User creates offer
        var userToken = JwtTestHelper.GenerateToken("comment-owner", "owner@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var createRes = await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>();

        // Admin adds comment
        var adminToken = JwtTestHelper.GenerateToken("admin-user-id", "admin@test.com", isAdmin: true);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var commentRes = await _client.PostAsJsonAsync(
            $"/api/job-offers/{created!.Id}/comments", new { content = "Thanks, reviewing now!" });
        Assert.Equal(HttpStatusCode.NoContent, commentRes.StatusCode);

        // Verify as user
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var listRes = await _client.GetFromJsonAsync<ListCommentsResponse>(
            $"/api/job-offers/{created.Id}/comments");
        Assert.Single(listRes!.Comments);
        Assert.Equal("Thanks, reviewing now!", listRes.Comments[0].Content);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static CreateJobOfferRequest CreateValidRequest() =>
        new(
            CompanyName: "Acme Corp",
            ContactName: "John Doe",
            ContactEmail: "john@acme.com",
            JobTitle: "Senior Developer",
            Description: "We are looking for a senior developer to join our team.",
            SalaryRange: "$120k - $160k",
            Location: "Prague, CZ",
            IsRemote: true,
            AdditionalNotes: null,
            Attachments: null);
}
