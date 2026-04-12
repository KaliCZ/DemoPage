using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kalandra.Api.IntegrationTests.Helpers;

namespace Kalandra.Api.IntegrationTests.Features.JobOffers;

public class JobOfferApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ───── Auth ─────

    [Fact]
    public async Task Create_WithoutAuth_Returns401()
    {
        var response = await client.PostAsync("/api/job-offers", CreateValidFormContent(), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ───── Create ─────

    [Fact]
    public async Task Create_WithValidData_ReturnsDetailWithCorrectValues()
    {
        Authenticate();

        var response = await client.PostAsync("/api/job-offers", CreateValidFormContent(), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await ParseJsonAsync(response);
        AssertValidGuid(json, "id");
        Assert.Equal("Acme Corp", json.GetProperty("companyName").GetString());
        Assert.Equal("John Doe", json.GetProperty("contactName").GetString());
        Assert.Equal("john@acme.com", json.GetProperty("contactEmail").GetString());
        Assert.Equal("Senior Developer", json.GetProperty("jobTitle").GetString());
        Assert.Equal("We are looking for a senior developer to join our team.", json.GetProperty("description").GetString());
        Assert.Equal("$120k - $160k", json.GetProperty("salaryRange").GetString());
        Assert.Equal("Prague, CZ", json.GetProperty("location").GetString());
        Assert.True(json.GetProperty("isRemote").GetBoolean());
        Assert.Equal("Submitted", json.GetProperty("status").GetString());
        Assert.Equal("test@example.com", json.GetProperty("userEmail").GetString());
        Assert.Equal(0, json.GetProperty("attachments").GetArrayLength());
        AssertValidTimestamp(json, "createdAt");
        AssertValidTimestamp(json, "updatedAt");
    }

    [Fact]
    public async Task Create_WithInvalidData_Returns400()
    {
        Authenticate();

        var content = new MultipartFormDataContent
        {
            { new StringContent("test-token"), "cf-turnstile-response" },
            { new StringContent(""), "CompanyName" },
            { new StringContent(""), "ContactName" },
            { new StringContent(""), "ContactEmail" },
            { new StringContent(""), "JobTitle" },
            { new StringContent(""), "Description" },
            { new StringContent("false"), "IsRemote" },
        };

        var response = await client.PostAsync("/api/job-offers", content, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAttachments_ReturnsAttachmentInfo()
    {
        Authenticate();

        var content = CreateValidFormContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake pdf content"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "attachments", "portfolio.pdf");

        var response = await client.PostAsync("/api/job-offers", content, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await ParseJsonAsync(response);
        var attachments = json.GetProperty("attachments");
        Assert.Equal(1, attachments.GetArrayLength());

        var attachment = attachments[0];
        Assert.Equal("portfolio.pdf", attachment.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", attachment.GetProperty("contentType").GetString());
        Assert.True(attachment.GetProperty("fileSize").GetInt64() > 0);
        var storagePath = attachment.GetProperty("storagePath").GetString();
        Assert.NotNull(storagePath);
        Assert.NotEmpty(storagePath);
    }

    [Fact]
    public async Task Create_WithDisallowedFileType_Returns400()
    {
        Authenticate();

        var content = CreateValidFormContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("malicious script"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-sh");
        content.Add(fileContent, "attachments", "hack.sh");

        var response = await client.PostAsync("/api/job-offers", content, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ───── Get Detail ─────

    [Fact]
    public async Task GetDetail_AsOtherUser_Returns404()
    {
        var (id, _) = await CreateOfferAs("viewowner@test.com");

        Authenticate("viewother@test.com");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/job-offers/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/job-offers/{id}/history", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/job-offers/{id}/comments", Ct)).StatusCode);
    }

    [Fact]
    public async Task GetDetail_AsAdmin_ReturnsOffer()
    {
        var (id, _) = await CreateOfferAs("detail@test.com");

        AuthenticateAs(AdminUserId, "admin@test.com", isAdmin: true);

        var response = await client.GetAsync($"/api/job-offers/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.Equal(id, json.GetProperty("id").GetString());
        Assert.Equal("Acme Corp", json.GetProperty("companyName").GetString());
    }

    // ───── List Mine ─────

    [Fact]
    public async Task ListMine_ReturnsOwnOffersOnly()
    {
        var (ownerId, _) = await CreateOfferAs("listowner@test.com");

        // Owner's list contains their offer
        var ownerListRes = await client.GetAsync("/api/job-offers/mine", Ct);
        Assert.Equal(HttpStatusCode.OK, ownerListRes.StatusCode);
        var ownerList = await ParseJsonAsync(ownerListRes);
        Assert.True(ownerList.GetProperty("totalCount").GetInt32() >= 1);
        Assert.Contains(
            ownerList.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetString() == ownerId);

        // Other user's list does not contain it
        Authenticate("listother@test.com");
        var otherListRes = await client.GetAsync("/api/job-offers/mine", Ct);
        Assert.Equal(HttpStatusCode.OK, otherListRes.StatusCode);
        var otherList = await ParseJsonAsync(otherListRes);
        Assert.DoesNotContain(
            otherList.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetString() == ownerId);
    }

    // ───── List All (Admin) ─────

    [Fact]
    public async Task ListAll_AsNonAdmin_Returns403()
    {
        Authenticate();
        var response = await client.GetAsync("/api/job-offers", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListAll_AsAdmin_ReturnsOffersWithStructure()
    {
        await CreateOfferAs("adminlist@test.com");

        AuthenticateAs(AdminUserId, "admin@test.com", isAdmin: true);
        var response = await client.GetAsync("/api/job-offers", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        Assert.True(json.GetProperty("totalCount").GetInt32() >= 1);
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);

        var first = items[0];
        AssertValidGuid(first, "id");
        Assert.NotNull(first.GetProperty("companyName").GetString());
        Assert.NotNull(first.GetProperty("jobTitle").GetString());
        Assert.NotNull(first.GetProperty("status").GetString());
        AssertValidTimestamp(first, "createdAt");
    }

    // ───── Edit ─────

    [Fact]
    public async Task Edit_AsOwner_WhenSubmitted_UpdatesOfferAndHistory()
    {
        var (id, _) = await CreateOfferAs("edit@test.com");

        var editRes = await client.PatchAsJsonAsync(
            $"/api/job-offers/{id}",
            new
            {
                companyName = "Updated Corp",
                contactName = "Jane Doe",
                contactEmail = "jane@updated.com",
                jobTitle = "CTO",
                description = "Updated description.",
                salaryRange = "$200k",
                location = "Remote",
                isRemote = true,
                additionalNotes = (string?)null,
            },
            Ct);
        Assert.Equal(HttpStatusCode.OK, editRes.StatusCode);

        // Verify response reflects new values
        var edited = await ParseJsonAsync(editRes);
        Assert.Equal("Updated Corp", edited.GetProperty("companyName").GetString());
        Assert.Equal("Jane Doe", edited.GetProperty("contactName").GetString());
        Assert.Equal("jane@updated.com", edited.GetProperty("contactEmail").GetString());
        Assert.Equal("CTO", edited.GetProperty("jobTitle").GetString());
        Assert.Equal("Updated description.", edited.GetProperty("description").GetString());
        Assert.Equal("$200k", edited.GetProperty("salaryRange").GetString());
        Assert.Equal("Submitted", edited.GetProperty("status").GetString());

        // Verify detail endpoint also reflects edit
        var detailRes = await client.GetAsync($"/api/job-offers/{id}", Ct);
        var detail = await ParseJsonAsync(detailRes);
        Assert.Equal("Updated Corp", detail.GetProperty("companyName").GetString());
        Assert.Equal("CTO", detail.GetProperty("jobTitle").GetString());

        // Verify history has Edited event
        var historyRes = await client.GetAsync($"/api/job-offers/{id}/history", Ct);
        var history = await ParseJsonAsync(historyRes);
        var entries = history.GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal("Submitted", entries[0].GetProperty("eventType").GetString());
        Assert.Equal("Edited", entries[1].GetProperty("eventType").GetString());
        Assert.Equal("edit@test.com", entries[1].GetProperty("actorEmail").GetString());
    }

    [Fact]
    public async Task Edit_PartialUpdate_OnlyChangesProvidedFields()
    {
        var (id, _) = await CreateOfferAs("partialedit@test.com");

        // Send only jobTitle — all other fields should remain unchanged
        var editRes = await client.PatchAsJsonAsync(
            $"/api/job-offers/{id}",
            new { jobTitle = "Principal Engineer" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, editRes.StatusCode);

        var edited = await ParseJsonAsync(editRes);
        Assert.Equal("Acme Corp", edited.GetProperty("companyName").GetString());
        Assert.Equal("John Doe", edited.GetProperty("contactName").GetString());
        Assert.Equal("john@acme.com", edited.GetProperty("contactEmail").GetString());
        Assert.Equal("Principal Engineer", edited.GetProperty("jobTitle").GetString());
        Assert.Equal("We are looking for a senior developer to join our team.", edited.GetProperty("description").GetString());
        Assert.Equal("$120k - $160k", edited.GetProperty("salaryRange").GetString());
        Assert.Equal("Prague, CZ", edited.GetProperty("location").GetString());
        Assert.True(edited.GetProperty("isRemote").GetBoolean());
        Assert.Equal("Submitted", edited.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Edit_AsOtherUser_Returns403()
    {
        var (id, _) = await CreateOfferAs("editowner@test.com");

        Authenticate("editother@test.com");
        var editRes = await client.PatchAsJsonAsync(
            $"/api/job-offers/{id}",
            CreateValidEditBody(),
            Ct);
        Assert.Equal(HttpStatusCode.Forbidden, editRes.StatusCode);
    }

    [Fact]
    public async Task Edit_WhenNotSubmitted_Returns400()
    {
        var (id, _) = await CreateOfferAs("editfail@test.com");

        await client.PostAsJsonAsync($"/api/job-offers/{id}/cancel", new { reason = "" }, Ct);

        var editRes = await client.PatchAsJsonAsync($"/api/job-offers/{id}", CreateValidEditBody(), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, editRes.StatusCode);
    }

    // ───── Cancel ─────

    [Fact]
    public async Task Cancel_AsOwner_SetsCancelledStatusAndHistory()
    {
        var (id, _) = await CreateOfferAs("cancel@test.com");

        var cancelRes = await client.PostAsJsonAsync(
            $"/api/job-offers/{id}/cancel",
            new { reason = "Changed my mind" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        var cancelled = await ParseJsonAsync(cancelRes);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal(id, cancelled.GetProperty("id").GetString());

        // Verify history
        var historyRes = await client.GetAsync($"/api/job-offers/{id}/history", Ct);
        var history = await ParseJsonAsync(historyRes);
        var entries = history.GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal("Submitted", entries[0].GetProperty("eventType").GetString());
        Assert.Equal("Cancelled", entries[1].GetProperty("eventType").GetString());
        Assert.Equal("cancel@test.com", entries[1].GetProperty("actorEmail").GetString());
    }

    [Fact]
    public async Task Cancel_AsOtherUser_Returns403()
    {
        var (id, _) = await CreateOfferAs("owner@test.com");

        Authenticate("other@test.com");
        var cancelRes = await client.PostAsJsonAsync(
            $"/api/job-offers/{id}/cancel",
            new { reason = "Trying to cancel someone else's offer" },
            Ct);
        Assert.Equal(HttpStatusCode.Forbidden, cancelRes.StatusCode);
    }

    [Fact]
    public async Task Cancel_WhenAlreadyCancelled_Returns400()
    {
        var (id, _) = await CreateOfferAs("doublecancel@test.com");

        var firstCancel = await client.PostAsJsonAsync(
            $"/api/job-offers/{id}/cancel",
            new { reason = "First cancel" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, firstCancel.StatusCode);

        var secondCancel = await client.PostAsJsonAsync(
            $"/api/job-offers/{id}/cancel",
            new { reason = "Second cancel" },
            Ct);
        Assert.Equal(HttpStatusCode.BadRequest, secondCancel.StatusCode);
    }

    // ───── Admin Status Change ─────

    [Fact]
    public async Task AdminChangeStatus_UpdatesStatusAndHistory()
    {
        var (id, _) = await CreateOfferAs("status@test.com");

        AuthenticateAs(AdminUserId, "admin@test.com", isAdmin: true);

        var statusRes = await client.PatchAsJsonAsync(
            $"/api/job-offers/{id}/status",
            new { status = "InReview", adminNotes = "Looks promising" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);

        var updated = await ParseJsonAsync(statusRes);
        Assert.Equal("InReview", updated.GetProperty("status").GetString());
        Assert.Equal(id, updated.GetProperty("id").GetString());

        // Verify history
        var historyRes = await client.GetAsync($"/api/job-offers/{id}/history", Ct);
        var history = await ParseJsonAsync(historyRes);
        var entries = history.GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal("Submitted", entries[0].GetProperty("eventType").GetString());
        Assert.Equal("StatusChanged", entries[1].GetProperty("eventType").GetString());
        Assert.Equal("admin@test.com", entries[1].GetProperty("actorEmail").GetString());
    }

    [Fact]
    public async Task AdminCannotSetCancelled_ThroughStatusEndpoint()
    {
        var (id, _) = await CreateOfferAs("cancelstatus@test.com");

        AuthenticateAs(AdminUserId, "admin@test.com", isAdmin: true);

        var statusRes = await client.PatchAsJsonAsync(
            $"/api/job-offers/{id}/status",
            new { status = "Cancelled", adminNotes = "Force cancelling" },
            Ct);
        Assert.Equal(HttpStatusCode.BadRequest, statusRes.StatusCode);
    }

    [Fact]
    public async Task AdminCannotReopen_TerminalStatus()
    {
        var (id, _) = await CreateOfferAs("terminal@test.com");

        AuthenticateAs(AdminUserId, "admin@test.com", isAdmin: true);

        var declineRes = await client.PatchAsJsonAsync(
            $"/api/job-offers/{id}/status",
            new { status = "Declined", adminNotes = "No fit" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, declineRes.StatusCode);

        var reopenRes = await client.PatchAsJsonAsync(
            $"/api/job-offers/{id}/status",
            new { status = "InReview", adminNotes = "Actually, let's reopen this" },
            Ct);
        Assert.Equal(HttpStatusCode.BadRequest, reopenRes.StatusCode);
    }

    // ───── Comments ─────

    [Fact]
    public async Task Comments_OwnerCanAddAndList()
    {
        var (id, commentUserId) = await CreateOfferAs("comment@test.com");

        var commentRes = await client.PostAsJsonAsync(
            $"/api/job-offers/{id}/comments",
            new { content = "Hello, any update?" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, commentRes.StatusCode);

        var comment = await ParseJsonAsync(commentRes);
        AssertValidGuid(comment, "id");
        Assert.Equal(commentUserId.ToString(), comment.GetProperty("userId").GetString());
        Assert.Equal("comment@test.com", comment.GetProperty("userEmail").GetString());
        Assert.Equal("Hello, any update?", comment.GetProperty("content").GetString());
        AssertValidTimestamp(comment, "createdAt");

        // List comments
        var listRes = await client.GetAsync($"/api/job-offers/{id}/comments", Ct);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var comments = await ParseJsonAsync(listRes);
        var commentArray = comments.GetProperty("comments");
        Assert.Equal(1, commentArray.GetArrayLength());
        Assert.Equal("Hello, any update?", commentArray[0].GetProperty("content").GetString());
        Assert.Equal(commentUserId.ToString(), commentArray[0].GetProperty("userId").GetString());
    }

    [Fact]
    public async Task Comments_AdminCanReply()
    {
        var (id, ownerId) = await CreateOfferAs("owner@test.com");

        AuthenticateAs(AdminUserId, "admin@test.com", isAdmin: true);

        var commentRes = await client.PostAsJsonAsync(
            $"/api/job-offers/{id}/comments",
            new { content = "Thanks, reviewing now!" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, commentRes.StatusCode);

        var comment = await ParseJsonAsync(commentRes);
        Assert.Equal(AdminUserId.ToString(), comment.GetProperty("userId").GetString());
        Assert.Equal("admin@test.com", comment.GetProperty("userEmail").GetString());
        Assert.Equal("Thanks, reviewing now!", comment.GetProperty("content").GetString());

        // Verify visible to owner
        AuthenticateAs(ownerId, "owner@test.com");
        var listRes = await client.GetAsync($"/api/job-offers/{id}/comments", Ct);
        var comments = await ParseJsonAsync(listRes);
        var commentArray = comments.GetProperty("comments");
        Assert.Equal(1, commentArray.GetArrayLength());
        Assert.Equal("Thanks, reviewing now!", commentArray[0].GetProperty("content").GetString());
        Assert.Equal(AdminUserId.ToString(), commentArray[0].GetProperty("userId").GetString());
    }

    // ───── Attachments ─────

    [Fact]
    public async Task DownloadAttachment_RoundTrips_MatchingContent()
    {
        Authenticate();

        var originalContent = "round-trip test content";
        var content = CreateValidFormContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(originalContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "attachments", "roundtrip.pdf");

        var createResponse = await client.PostAsync("/api/job-offers", content, Ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ParseJsonAsync(createResponse);
        var id = AssertValidGuid(created, "id");

        var downloadResponse = await client.GetAsync(
            $"/api/job-offers/{id}/attachments/{Uri.EscapeDataString("roundtrip.pdf")}", Ct);
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);

        var downloadedContent = await downloadResponse.Content.ReadAsStringAsync(Ct);
        Assert.Equal(originalContent, downloadedContent);
    }

    [Fact]
    public async Task DownloadAttachment_NonExistentFile_Returns404()
    {
        var (id, _) = await CreateOfferAs("attach@test.com");

        var downloadResponse = await client.GetAsync(
            $"/api/job-offers/{id}/attachments/{Uri.EscapeDataString("nonexistent.pdf")}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, downloadResponse.StatusCode);
    }

    // ───── Health ─────

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ───── Helpers ─────

    private static readonly Guid AdminUserId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private Guid Authenticate(
        string email = "test@example.com",
        bool isAdmin = false)
    {
        var userId = Guid.NewGuid();
        var token = JwtTestHelper.GenerateToken(userId, email, isAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return userId;
    }

    private void AuthenticateAs(Guid userId, string email, bool isAdmin = false)
    {
        var token = JwtTestHelper.GenerateToken(userId, email, isAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Creates an offer as a fresh user and returns the new offer's id + the user's id.
    /// Leaves the client authenticated as that user.
    /// </summary>
    private async Task<(string OfferId, Guid UserId)> CreateOfferAs(string email)
    {
        var userId = Authenticate(email);
        var response = await client.PostAsync("/api/job-offers", CreateValidFormContent(), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await ParseJsonAsync(response);
        return (AssertValidGuid(json, "id"), userId);
    }

    private static string AssertValidGuid(JsonElement parent, string propertyName)
    {
        var raw = parent.GetProperty(propertyName).GetString();
        Assert.NotNull(raw);
        Assert.NotEqual(Guid.Empty, Guid.Parse(raw));
        return raw;
    }

    private static void AssertValidTimestamp(JsonElement parent, string propertyName)
    {
        var raw = parent.GetProperty(propertyName).GetString();
        Assert.NotNull(raw);
        Assert.True(
            DateTimeOffset.TryParse(raw, out var parsed) && parsed > DateTimeOffset.UnixEpoch,
            $"Expected valid timestamp for '{propertyName}', got '{raw}'");
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(), cancellationToken: CancellationToken.None);
        return doc.RootElement;
    }

    private static MultipartFormDataContent CreateValidFormContent()
    {
        return new MultipartFormDataContent
        {
            { new StringContent("test-token"), "cf-turnstile-response" },
            { new StringContent("Acme Corp"), "CompanyName" },
            { new StringContent("John Doe"), "ContactName" },
            { new StringContent("john@acme.com"), "ContactEmail" },
            { new StringContent("Senior Developer"), "JobTitle" },
            { new StringContent("We are looking for a senior developer to join our team."), "Description" },
            { new StringContent("$120k - $160k"), "SalaryRange" },
            { new StringContent("Prague, CZ"), "Location" },
            { new StringContent("true"), "IsRemote" },
        };
    }

    private static object CreateValidEditBody() =>
        new
        {
            companyName = "Acme Corp",
            contactName = "John Doe",
            contactEmail = "john@acme.com",
            jobTitle = "Senior Developer",
            description = "We are looking for a senior developer to join our team.",
            salaryRange = "$120k - $160k",
            location = "Prague, CZ",
            isRemote = true,
            additionalNotes = (string?)null,
        };
}
