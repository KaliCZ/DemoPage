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

    // ───── Link Email ─────

    [Fact]
    public async Task LinkEmail_WithoutAuth_Returns401()
    {
        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "test123456" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LinkEmail_WithShortPassword_Returns400()
    {
        Authenticate("link-short-pw", "short@test.com");
        adminService.SeedUser("link-short-pw", "short@test.com", "google");

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "abc" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Contains("6 characters", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task LinkEmail_WithEmptyPassword_Returns400()
    {
        Authenticate("link-empty-pw", "empty@test.com");
        adminService.SeedUser("link-empty-pw", "empty@test.com", "google");

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LinkEmail_WithValidPassword_Returns200AndLinksIdentity()
    {
        Authenticate("link-valid", "valid@test.com");
        adminService.SeedUser("link-valid", "valid@test.com", "google");

        var response = await client.PostAsJsonAsync("/api/auth/link-email", new { password = "securepassword123" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Contains("successfully", json.GetProperty("message").GetString());

        // Verify the identity was added
        var user = await adminService.GetUserAsync("link-valid", Ct);
        Assert.NotNull(user);
        var identities = user.Value.GetProperty("identities").EnumerateArray().ToList();
        Assert.Equal(2, identities.Count);
        Assert.Contains(identities, i => i.GetProperty("provider").GetString() == "google");
        Assert.Contains(identities, i => i.GetProperty("provider").GetString() == "email");
    }

    // ───── Get Identities ─────

    [Fact]
    public async Task GetIdentities_WithoutAuth_Returns401()
    {
        var response = await client.GetAsync("/api/auth/identities", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetIdentities_ReturnsUserIdentities()
    {
        Authenticate("get-ids", "ids@test.com");
        adminService.SeedUser("get-ids", "ids@test.com", "google", "email");

        var response = await client.GetAsync("/api/auth/identities", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        var identities = json.GetProperty("identities").EnumerateArray().ToList();
        Assert.Equal(2, identities.Count);
        Assert.Contains(identities, i => i.GetProperty("provider").GetString() == "google");
        Assert.Contains(identities, i => i.GetProperty("provider").GetString() == "email");
    }

    [Fact]
    public async Task GetIdentities_WhenUserNotFound_Returns404()
    {
        Authenticate("nonexistent-user", "noone@test.com");
        // Don't seed the user

        var response = await client.GetAsync("/api/auth/identities", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ───── Unlink Identity ─────

    [Fact]
    public async Task UnlinkIdentity_WithoutAuth_Returns401()
    {
        var response = await client.DeleteAsync("/api/auth/identities/some-id", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnlinkIdentity_RemovesIdentity()
    {
        Authenticate("unlink-user", "unlink@test.com");
        adminService.SeedUser("unlink-user", "unlink@test.com", "google", "email");

        // Get the google identity id
        var user = await adminService.GetUserAsync("unlink-user", Ct);
        var googleIdentity = user!.Value.GetProperty("identities")
            .EnumerateArray()
            .First(i => i.GetProperty("provider").GetString() == "google");
        var googleIdentityId = googleIdentity.GetProperty("id").GetString()!;

        var response = await client.DeleteAsync($"/api/auth/identities/{googleIdentityId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify only email identity remains
        var updatedUser = await adminService.GetUserAsync("unlink-user", Ct);
        var identities = updatedUser!.Value.GetProperty("identities").EnumerateArray().ToList();
        Assert.Single(identities);
        Assert.Equal("email", identities[0].GetProperty("provider").GetString());
    }

    [Fact]
    public async Task UnlinkIdentity_WhenLastIdentity_Returns400()
    {
        Authenticate("last-id-user", "last@test.com");
        adminService.SeedUser("last-id-user", "last@test.com", "email");

        var user = await adminService.GetUserAsync("last-id-user", Ct);
        var identityId = user!.Value.GetProperty("identities")
            .EnumerateArray().First()
            .GetProperty("id").GetString()!;

        var response = await client.DeleteAsync($"/api/auth/identities/{identityId}", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Contains("last identity", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task UnlinkIdentity_WhenIdentityNotFound_Returns404()
    {
        Authenticate("no-id-user", "noid@test.com");
        adminService.SeedUser("no-id-user", "noid@test.com", "email");

        var response = await client.DeleteAsync("/api/auth/identities/nonexistent-identity-id", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ───── Full Flow: Link → Verify → Unlink → Verify → Relink ─────

    [Fact]
    public async Task FullFlow_LinkUnlinkRelink()
    {
        var userId = "full-flow-user";
        var email = "fullflow@test.com";
        Authenticate(userId, email);
        adminService.SeedUser(userId, email, "google");

        // 1. Verify only google identity
        var identitiesRes = await client.GetAsync("/api/auth/identities", Ct);
        var identities = (await ParseJsonAsync(identitiesRes))
            .GetProperty("identities").EnumerateArray().ToList();
        Assert.Single(identities);
        Assert.Equal("google", identities[0].GetProperty("provider").GetString());

        // 2. Link email identity
        var linkRes = await client.PostAsJsonAsync(
            "/api/auth/link-email",
            new { password = "securepassword123" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, linkRes.StatusCode);

        // 3. Verify 2 identities
        identitiesRes = await client.GetAsync("/api/auth/identities", Ct);
        identities = (await ParseJsonAsync(identitiesRes))
            .GetProperty("identities").EnumerateArray().ToList();
        Assert.Equal(2, identities.Count);
        Assert.Contains(identities, i => i.GetProperty("provider").GetString() == "google");
        Assert.Contains(identities, i => i.GetProperty("provider").GetString() == "email");

        // 4. Unlink google
        var googleId = identities
            .First(i => i.GetProperty("provider").GetString() == "google")
            .GetProperty("id").GetString()!;
        var unlinkRes = await client.DeleteAsync($"/api/auth/identities/{googleId}", Ct);
        Assert.Equal(HttpStatusCode.OK, unlinkRes.StatusCode);

        // 5. Verify only email identity remains
        identitiesRes = await client.GetAsync("/api/auth/identities", Ct);
        identities = (await ParseJsonAsync(identitiesRes))
            .GetProperty("identities").EnumerateArray().ToList();
        Assert.Single(identities);
        Assert.Equal("email", identities[0].GetProperty("provider").GetString());

        // 6. Re-link google (simulate by adding via admin update)
        // In real scenario this would be OAuth flow, but we test the admin service behavior
        adminService.SeedUser(userId, email, "email", "google");

        // 7. Verify 2 identities again
        identitiesRes = await client.GetAsync("/api/auth/identities", Ct);
        identities = (await ParseJsonAsync(identitiesRes))
            .GetProperty("identities").EnumerateArray().ToList();
        Assert.Equal(2, identities.Count);
        Assert.Contains(identities, i => i.GetProperty("provider").GetString() == "google");
        Assert.Contains(identities, i => i.GetProperty("provider").GetString() == "email");
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
