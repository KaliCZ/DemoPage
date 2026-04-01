using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kalandra.Api.Features.JobOffers.Comments;
using Kalandra.Api.Features.JobOffers.Create;
using Kalandra.Api.Features.JobOffers.Edit;
using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.GetDetail;
using Kalandra.Api.Features.JobOffers.History;
using Kalandra.Api.Features.JobOffers.List;
using Kalandra.Api.Tests.Helpers;

namespace Kalandra.Api.Tests.Features.JobOffers;

public class JobOfferApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_WithoutAuth_Returns401()
    {
        var response = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAuth_Returns201()
    {
        var token = JwtTestHelper.GenerateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Create_WithInvalidData_Returns400()
    {
        var token = JwtTestHelper.GenerateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new CreateJobOfferRequest(null, "", "", "", "", "", null, null, false, null, null);
        var response = await client.PostAsJsonAsync("/api/job-offers", request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListAll_AsNonAdmin_Returns403()
    {
        var token = JwtTestHelper.GenerateToken("regular-user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/job-offers", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListAll_AsAdmin_Returns200()
    {
        var token = JwtTestHelper.GenerateToken("admin-user-id", "admin@test.com", isAdmin: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/job-offers", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_SubmitCancelAndVerifyHistory()
    {
        var token = JwtTestHelper.GenerateToken("lifecycle-user", "lifecycle@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Submit
        var createResponse = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);
        var id = created!.Id;

        // List mine
        var listResponse = await client.GetAsync("/api/job-offers/mine", Ct);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<ListJobOffersResponse>(cancellationToken: Ct);
        Assert.Contains(list!.Items, i => i.Id == id);

        // Get detail
        var detailResponse = await client.GetAsync($"/api/job-offers/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        // Get history
        var historyResponse = await client.GetAsync($"/api/job-offers/{id}/history", Ct);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<JobOfferHistoryResponse>(cancellationToken: Ct);
        Assert.Single(history!.Entries);
        Assert.Equal("Submitted", history.Entries[0].EventType);

        // Cancel
        var cancelResponse = await client.PostAsJsonAsync($"/api/job-offers/{id}/cancel",
            new { reason = "Made a mistake" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

        // Verify history after cancel
        var historyAfter = await client.GetAsync($"/api/job-offers/{id}/history", Ct);
        var historyData = await historyAfter.Content.ReadFromJsonAsync<JobOfferHistoryResponse>(cancellationToken: Ct);
        Assert.Equal(2, historyData!.Entries.Count);
        Assert.Equal("Cancelled", historyData.Entries[1].EventType);
    }

    [Fact]
    public async Task AdminCanChangeStatus_AndHistoryRecords()
    {
        // Create as regular user
        var userToken = JwtTestHelper.GenerateToken("status-user", "status@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var createResponse = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);
        var id = created!.Id;

        // Change status as admin
        var adminToken = JwtTestHelper.GenerateToken("admin-user-id", "admin@test.com", isAdmin: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var statusResponse = await client.PatchAsJsonAsync($"/api/job-offers/{id}/status",
            new { status = 1, adminNotes = "Looks promising" }, Ct); // InReview = 1
        Assert.Equal(HttpStatusCode.NoContent, statusResponse.StatusCode);

        // Verify history
        var historyResponse = await client.GetAsync($"/api/job-offers/{id}/history", Ct);
        var history = await historyResponse.Content.ReadFromJsonAsync<JobOfferHistoryResponse>(cancellationToken: Ct);
        Assert.Equal(2, history!.Entries.Count);
        Assert.Equal("StatusChanged", history.Entries[1].EventType);
    }

    [Fact]
    public async Task AdminCannotSetCancelledStatus_ThroughAdminEndpoint()
    {
        var userToken = JwtTestHelper.GenerateToken("status-user", "status@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var createResponse = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);

        var adminToken = JwtTestHelper.GenerateToken("admin-user-id", "admin@test.com", isAdmin: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var statusResponse = await client.PatchAsJsonAsync($"/api/job-offers/{created!.Id}/status",
            new { status = 4, adminNotes = "Force cancelling" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, statusResponse.StatusCode);
    }

    [Fact]
    public async Task AdminCannotReopenTerminalStatus()
    {
        var userToken = JwtTestHelper.GenerateToken("terminal-user", "terminal@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var createResponse = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);

        var adminToken = JwtTestHelper.GenerateToken("admin-user-id", "admin@test.com", isAdmin: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var declineResponse = await client.PatchAsJsonAsync($"/api/job-offers/{created!.Id}/status",
            new { status = 3, adminNotes = "No fit" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, declineResponse.StatusCode);

        var reopenResponse = await client.PatchAsJsonAsync($"/api/job-offers/{created.Id}/status",
            new { status = 1, adminNotes = "Actually, let's reopen this" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, reopenResponse.StatusCode);
    }

    [Fact]
    public async Task CannotCancel_OtherUsersOffer()
    {
        // Create as user1
        var token1 = JwtTestHelper.GenerateToken("owner-user", "owner@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var createResponse = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);

        // Try to cancel as user2
        var token2 = JwtTestHelper.GenerateToken("other-user", "other@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var cancelResponse = await client.PostAsJsonAsync($"/api/job-offers/{created!.Id}/cancel",
            new { reason = "Trying to cancel someone else's offer" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task CannotView_OtherUsersOffer()
    {
        // Create as user1
        var token1 = JwtTestHelper.GenerateToken("view-owner", "viewowner@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var createRes = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);

        // Try to view as user2
        var token2 = JwtTestHelper.GenerateToken("view-other", "viewother@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var detailRes = await client.GetAsync($"/api/job-offers/{created!.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, detailRes.StatusCode);

        var historyRes = await client.GetAsync($"/api/job-offers/{created.Id}/history", Ct);
        Assert.Equal(HttpStatusCode.NotFound, historyRes.StatusCode);

        var commentsRes = await client.GetAsync($"/api/job-offers/{created.Id}/comments", Ct);
        Assert.Equal(HttpStatusCode.NotFound, commentsRes.StatusCode);
    }

    [Fact]
    public async Task CannotEdit_OtherUsersOffer()
    {
        // Create as user1
        var token1 = JwtTestHelper.GenerateToken("edit-owner", "editowner@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var createRes = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);

        // Try to edit as user2
        var token2 = JwtTestHelper.GenerateToken("edit-other", "editother@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var editRes = await client.PutAsJsonAsync($"/api/job-offers/{created!.Id}", EditValidRequest(), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, editRes.StatusCode);
    }

    [Fact]
    public async Task ListMine_DoesNotShowOtherUsersOffers()
    {
        // Create as user1
        var token1 = JwtTestHelper.GenerateToken("list-owner", "listowner@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var createRes = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);

        // List as user2
        var token2 = JwtTestHelper.GenerateToken("list-other", "listother@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var listRes = await client.GetAsync("/api/job-offers/mine", Ct);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await listRes.Content.ReadFromJsonAsync<ListJobOffersResponse>(cancellationToken: Ct);
        Assert.DoesNotContain(list!.Items, i => i.Id == created!.Id);
    }

    [Fact]
    public async Task Edit_WhenSubmitted_Succeeds()
    {
        var token = JwtTestHelper.GenerateToken("edit-user", "edit@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create
        var createRes = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);

        // Edit
        var editRequest = new EditJobOfferRequest(
            "Updated Corp", "Jane Doe", "jane@updated.com", "CTO",
            "Updated description.", "$200k", "Remote", true, null);
        var editRes = await client.PutAsJsonAsync($"/api/job-offers/{created!.Id}", editRequest, Ct);
        Assert.Equal(HttpStatusCode.NoContent, editRes.StatusCode);

        // Verify detail reflects edit
        var detail = await client.GetFromJsonAsync<GetJobOfferDetailResponse>($"/api/job-offers/{created.Id}", Ct);
        Assert.Equal("Updated Corp", detail!.CompanyName);
        Assert.Equal("CTO", detail.JobTitle);

        // Verify history has Edited event
        var history = await client.GetFromJsonAsync<JobOfferHistoryResponse>($"/api/job-offers/{created.Id}/history", Ct);
        Assert.Equal(2, history!.Entries.Count);
        Assert.Equal("Edited", history.Entries[1].EventType);
    }

    [Fact]
    public async Task Edit_WhenNotSubmitted_Fails()
    {
        // Create and cancel
        var token = JwtTestHelper.GenerateToken("edit-fail-user", "editfail@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRes = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);

        await client.PostAsJsonAsync($"/api/job-offers/{created!.Id}/cancel", new { reason = "" }, Ct);

        // Try to edit cancelled offer
        var editRes = await client.PutAsJsonAsync($"/api/job-offers/{created.Id}", EditValidRequest(), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, editRes.StatusCode);
    }

    [Fact]
    public async Task Comments_OwnerCanAddAndList()
    {
        var token = JwtTestHelper.GenerateToken("comment-user", "comment@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRes = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);

        // Add comment
        var commentRes = await client.PostAsJsonAsync(
            $"/api/job-offers/{created!.Id}/comments", new { content = "Hello, any update?" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, commentRes.StatusCode);

        // List comments
        var listRes = await client.GetAsync($"/api/job-offers/{created.Id}/comments", Ct);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var comments = await listRes.Content.ReadFromJsonAsync<ListCommentsResponse>(cancellationToken: Ct);
        Assert.Single(comments!.Comments);
        Assert.Equal("Hello, any update?", comments.Comments[0].Content);
    }

    [Fact]
    public async Task Comments_AdminCanReply()
    {
        // User creates offer
        var userToken = JwtTestHelper.GenerateToken("comment-owner", "owner@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var createRes = await client.PostAsJsonAsync("/api/job-offers", CreateValidRequest(), Ct);
        var created = await createRes.Content.ReadFromJsonAsync<CreateJobOfferResponse>(cancellationToken: Ct);

        // Admin adds comment
        var adminToken = JwtTestHelper.GenerateToken("admin-user-id", "admin@test.com", isAdmin: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var commentRes = await client.PostAsJsonAsync(
            $"/api/job-offers/{created!.Id}/comments", new { content = "Thanks, reviewing now!" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, commentRes.StatusCode);

        // Verify as user
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var listRes = await client.GetFromJsonAsync<ListCommentsResponse>(
            $"/api/job-offers/{created.Id}/comments", Ct);
        Assert.Single(listRes!.Comments);
        Assert.Equal("Thanks, reviewing now!", listRes.Comments[0].Content);
    }

    [Fact]
    public async Task Create_WithVerifiedAttachments_Returns201()
    {
        var token = JwtTestHelper.GenerateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var offerId = Guid.NewGuid();
        var request = new CreateJobOfferRequest(
            offerId,
            "Acme Corp",
            "John Doe",
            "john@acme.com",
            "Senior Developer",
            "We are looking for a senior developer to join our team.",
            "$120k - $160k",
            "Prague, CZ",
            true,
            null,
            [
                new AttachmentInfo(
                    "portfolio.pdf",
                    $"test-user-id/{offerId}/portfolio.pdf",
                    1024,
                    "application/pdf")
            ]);

        var response = await client.PostAsJsonAsync("/api/job-offers", request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAttachmentOutsideOfferPath_Returns400()
    {
        var token = JwtTestHelper.GenerateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var offerId = Guid.NewGuid();
        var request = new CreateJobOfferRequest(
            offerId,
            "Acme Corp",
            "John Doe",
            "john@acme.com",
            "Senior Developer",
            "We are looking for a senior developer to join our team.",
            "$120k - $160k",
            "Prague, CZ",
            true,
            null,
            [
                new AttachmentInfo(
                    "portfolio.pdf",
                    $"test-user-id/{Guid.NewGuid()}/portfolio.pdf",
                    1024,
                    "application/pdf")
            ]);

        var response = await client.PostAsJsonAsync("/api/job-offers", request, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static CreateJobOfferRequest CreateValidRequest() =>
        new(
            Id: null,
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

    private static EditJobOfferRequest EditValidRequest() =>
        new(
            CompanyName: "Acme Corp",
            ContactName: "John Doe",
            ContactEmail: "john@acme.com",
            JobTitle: "Senior Developer",
            Description: "We are looking for a senior developer to join our team.",
            SalaryRange: "$120k - $160k",
            Location: "Prague, CZ",
            IsRemote: true,
            AdditionalNotes: null);
}
