using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kalandra.Api.Tests.Helpers;

namespace Kalandra.Api.Tests.Features.Auth;

public class AuthApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private readonly FakeSupabaseAdminService adminService = factory.FakeAdminService;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ───── Auth ─────

    [Fact]
    public async Task LinkEmail_WithoutAuth_Returns401()
    {
        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "test123456" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ───── Validation ─────

    [Fact]
    public async Task LinkEmail_WithShortPassword_Returns400()
    {
        Authenticate();

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "abc" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Contains("6 characters", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task LinkEmail_WithEmptyPassword_Returns400()
    {
        Authenticate();

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ───── Success ─────

    [Fact]
    public async Task LinkEmail_WithValidPassword_Returns200()
    {
        Authenticate("link-user", "link@test.com");
        adminService.NextCallSucceeds = true;

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "securepassword123" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Contains("successfully", json.GetProperty("message").GetString());

        Assert.NotNull(adminService.LastUpdateCall);
        Assert.Equal("link-user", adminService.LastUpdateCall.Value.UserId);
    }

    // ───── Supabase Failure ─────

    [Fact]
    public async Task LinkEmail_WhenSupabaseFails_Returns400WithError()
    {
        Authenticate("fail-user", "fail@test.com");
        adminService.NextCallSucceeds = false;
        adminService.NextCallError = "User already has email identity";

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "securepassword123" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Equal("User already has email identity", json.GetProperty("error").GetString());
    }

    // ───── Helpers ─────

    private void Authenticate(
        string userId = "test-user-id",
        string email = "test@example.com",
        bool isAdmin = false)
    {
        var token = JwtTestHelper.GenerateToken(userId, email, isAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(), cancellationToken: CancellationToken.None);
        return doc.RootElement;
    }
}
