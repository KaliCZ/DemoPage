using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Kalandra.Api.Tests.Helpers;

namespace Kalandra.Api.Tests.Features.Profile;

public class ProfileApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UploadAvatar_WithoutAuth_Returns401()
    {
        var content = BuildMultipart(bytes: new byte[100], contentType: "image/png", fileName: "avatar.png");
        var response = await client.PostAsync("/api/profile/avatar", content, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadAvatar_WithEmptyFile_Returns400()
    {
        Authenticate();
        var content = BuildMultipart(bytes: [], contentType: "image/png", fileName: "avatar.png");
        var response = await client.PostAsync("/api/profile/avatar", content, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadAvatar_WithDisallowedContentType_Returns400()
    {
        Authenticate();
        var content = BuildMultipart(bytes: new byte[100], contentType: "image/gif", fileName: "avatar.gif");
        var response = await client.PostAsync("/api/profile/avatar", content, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadAvatar_ExceedingMaxSize_Returns400()
    {
        Authenticate();
        // 1 MB + 1 byte
        var oversize = new byte[1 * 1024 * 1024 + 1];
        var content = BuildMultipart(bytes: oversize, contentType: "image/png", fileName: "avatar.png");
        var response = await client.PostAsync("/api/profile/avatar", content, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadAvatar_WithValidPng_Returns200WithAvatarUrl()
    {
        Authenticate();
        var content = BuildMultipart(bytes: new byte[1024], contentType: "image/png", fileName: "avatar.png");
        var response = await client.PostAsync("/api/profile/avatar", content, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("avatarUrl", json);
        Assert.Contains("/storage/v1/object/public/avatars/", json);
    }

    [Fact]
    public async Task UploadAvatar_WithValidJpeg_Returns200()
    {
        Authenticate();
        var content = BuildMultipart(bytes: new byte[2048], contentType: "image/jpeg", fileName: "avatar.jpg");
        var response = await client.PostAsync("/api/profile/avatar", content, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UploadAvatar_WithValidWebp_Returns200()
    {
        Authenticate();
        var content = BuildMultipart(bytes: new byte[2048], contentType: "image/webp", fileName: "avatar.webp");
        var response = await client.PostAsync("/api/profile/avatar", content, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAvatar_WithoutAuth_Returns401()
    {
        var response = await client.DeleteAsync("/api/profile/avatar", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAvatar_WithAuth_Returns204()
    {
        Authenticate();
        var response = await client.DeleteAsync("/api/profile/avatar", Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private void Authenticate(
        string userId = "test-user-id",
        string email = "test@example.com")
    {
        var token = JwtTestHelper.GenerateToken(userId, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static MultipartFormDataContent BuildMultipart(byte[] bytes, string contentType, string fileName)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }
}
