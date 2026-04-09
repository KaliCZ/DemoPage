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
    public async Task LinkEmail_WithShortPassword_Returns400_PasswordTooShort()
    {
        Authenticate();

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "abc" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Equal("PasswordTooShort", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task LinkEmail_WithEmptyPassword_Returns400_PasswordTooShort()
    {
        Authenticate();

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Equal("PasswordTooShort", json.GetProperty("error").GetString());
    }

    // ───── Success ─────

    [Fact]
    public async Task LinkEmail_WithValidPassword_Returns204()
    {
        var linkUserId = Authenticate("link@test.com");
        adminService.NextCallSucceeds = true;

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "securepassword123" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.NotNull(adminService.LastChangePasswordCall);
        Assert.Equal(linkUserId, adminService.LastChangePasswordCall.Value.User.Id);
        Assert.Equal("securepassword123", adminService.LastChangePasswordCall.Value.Password);
    }

    // ───── Supabase Failure ─────

    [Fact]
    public async Task LinkEmail_WhenAlreadyLinked_Returns400_AlreadyLinked()
    {
        Authenticate("already@test.com");
        adminService.NextCallSucceeds = false;
        adminService.NextCallError = "User already has email identity";

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "securepassword123" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Equal("AlreadyLinked", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task LinkEmail_WhenSupabaseFails_Returns400_Failed()
    {
        Authenticate("fail@test.com");
        adminService.NextCallSucceeds = false;
        adminService.NextCallError = "Something unexpected happened";

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "securepassword123" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Equal("Failed", json.GetProperty("error").GetString());
    }

    // ───── Helpers ─────

    private Guid Authenticate(
        string email = "test@example.com",
        bool isAdmin = false)
    {
        var userId = Guid.NewGuid();
        var token = JwtTestHelper.GenerateToken(userId, email, isAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return userId;
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(), cancellationToken: CancellationToken.None);
        return doc.RootElement;
    }
}
