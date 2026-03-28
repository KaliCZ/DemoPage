using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kalandra.Api.Features.JobOffers.Create;
using Kalandra.Api.Features.JobOffers.History;
using Kalandra.Api.Features.JobOffers.List;
using Kalandra.Api.Tests.Helpers;

namespace Kalandra.Api.Tests.Features.JobOffers;

public class CreateJobOfferTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CreateJobOfferTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

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

        var request = new CreateJobOfferRequest("", "", "", "", "", null, null, false, null);
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
        var token = JwtTestHelper.GenerateToken("admin-user-id", "admin@test.com");
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
        var adminToken = JwtTestHelper.GenerateToken("admin-user-id", "admin@test.com");
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
            AdditionalNotes: null);
}
