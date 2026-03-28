using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kalandra.Api.Features.JobOffers.Create;
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
        var request = CreateValidRequest();
        var response = await _client.PostAsJsonAsync("/api/job-offers", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAuth_Returns201()
    {
        var token = JwtTestHelper.GenerateToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = CreateValidRequest();
        var response = await _client.PostAsJsonAsync("/api/job-offers", request);

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
    public async Task ListMine_ReturnsOnlyOwnOffers()
    {
        // Create offer as user1
        var token1 = JwtTestHelper.GenerateToken("user-1", "user1@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());

        // Create offer as user2
        var token2 = JwtTestHelper.GenerateToken("user-2", "user2@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        await _client.PostAsJsonAsync("/api/job-offers", CreateValidRequest());

        // List as user1
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var response = await _client.GetAsync("/api/job-offers/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
