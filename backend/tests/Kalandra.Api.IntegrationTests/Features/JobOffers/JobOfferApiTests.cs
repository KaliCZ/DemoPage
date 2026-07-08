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

    [Theory]
    [InlineData("CompanyName", 200)]
    [InlineData("ContactName", 200)]
    [InlineData("JobTitle", 200)]
    [InlineData("ContactEmail", 254)] // Email caps at RFC 5321's 254 chars; value still has to look like an email, we exceed by lengthening the local-part
    [InlineData("Description", 5000)]
    [InlineData("SalaryRange", 100)]
    [InlineData("Location", 200)]
    [InlineData("AdditionalNotes", 2000)]
    public async Task Create_WithFieldOverMaxLength_Returns400_WithFieldError(string fieldName, int maxLength)
    {
        Authenticate();

        var oversized = fieldName == "ContactEmail"
            ? new string('a', maxLength) + "@example.com"
            : new string('a', maxLength + 1);

        var content = CreateValidFormContent(overrides: new() { [fieldName] = oversized });

        var response = await client.PostAsync("/api/job-offers", content, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertValidationFieldError(await ParseJsonAsync(response), fieldName);
    }

    [Fact]
    public async Task Create_WithInvalidEmailFormat_Returns400()
    {
        Authenticate();

        var content = CreateValidFormContent(overrides: new() { ["ContactEmail"] = "not-an-email" });

        var response = await client.PostAsync("/api/job-offers", content, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        AssertValidationFieldError(await ParseJsonAsync(response), "ContactEmail");
    }

    [Fact]
    public async Task Create_ResentWithTheSameClientId_IsStoredOnce()
    {
        Authenticate(email: "create-dedupe@test.com");
        var offerId = Guid.NewGuid();

        // An accidental resend (double-submit, retry) carries the same client-generated id.
        var first = await client.PostAsync("/api/job-offers",
            CreateValidFormContent(overrides: new() { ["Id"] = offerId.ToString() }), Ct);
        var second = await client.PostAsync("/api/job-offers",
            CreateValidFormContent(overrides: new() { ["Id"] = offerId.ToString() }), Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(offerId.ToString(), (await ParseJsonAsync(first)).GetProperty("id").GetString());
        Assert.Equal(offerId.ToString(), (await ParseJsonAsync(second)).GetProperty("id").GetString());

        var mine = await ParseJsonAsync(await client.GetAsync("/api/job-offers/mine", Ct));
        Assert.Equal(1, mine.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Create_WithAnotherUsersOfferId_Returns400_WithoutLeakingTheOffer()
    {
        var (id, _) = await CreateOfferAs("id-owner@test.com");

        Authenticate(email: "id-thief@test.com");
        var marker = $"Never-stored offer must not notify {Guid.NewGuid():N}";
        var response = await client.PostAsync("/api/job-offers",
            CreateValidFormContent(overrides: new() { ["Id"] = id, ["Description"] = marker }), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ParseJsonAsync(response);
        Assert.Equal("IdAlreadyUsed", problem.GetProperty("errors").GetProperty("Id")[0].GetString());

        // Grace period: a notification for the rejected submission would arrive moments later.
        await Task.Delay(1500, Ct);
        Assert.DoesNotContain(factory.EmailSender.Sent, m => m.TextBody.Value.Contains(marker));
    }

    [Fact]
    public async Task Edit_WithFieldOverMaxLength_Returns400_WithFieldError()
    {
        var (id, _) = await CreateOfferAs("editmax@test.com");

        var response = await client.PatchAsJsonAsync(
            $"/api/job-offers/{id}",
            new { description = new string('a', 5001) },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertValidationFieldError(await ParseJsonAsync(response), "Description");
    }

    [Fact]
    public async Task Edit_WithInvalidEmailFormat_Returns400()
    {
        var (id, _) = await CreateOfferAs("editemail@test.com");

        var response = await client.PatchAsJsonAsync(
            $"/api/job-offers/{id}",
            new { contactEmail = "not-an-email" },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Email's [JsonConverter] rejects bad formats at deserialization time, so
        // ASP.NET surfaces the failure under the JSON path key — not the property name.
        AssertValidationFieldError(await ParseJsonAsync(response), "$.contactEmail");
    }

    [Fact]
    public async Task AddComment_OverMaxLength_Returns400_WithFieldError()
    {
        var (id, _) = await CreateOfferAs("commentmax@test.com");

        var response = await client.PostAsJsonAsync(
            $"/api/job-offers/{id}/comments",
            new { content = new string('a', 5001) },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertValidationFieldError(await ParseJsonAsync(response), "Content");
    }

    [Fact]
    public async Task AddComment_AtMaxLength_Succeeds()
    {
        var (id, _) = await CreateOfferAs("commentboundary@test.com");

        var response = await client.PostAsJsonAsync(
            $"/api/job-offers/{id}/comments",
            new { content = new string('a', 5000) },
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddComment_ResentWithTheSameClientId_IsStoredOnce()
    {
        var (id, _) = await CreateOfferAs("comment-dedupe@test.com");
        var commentId = Guid.NewGuid();

        // An accidental resend (double-submit, retry) carries the same client-generated id.
        var first = await client.PostAsJsonAsync($"/api/job-offers/{id}/comments", new { commentId, content = "Only once" }, Ct);
        var second = await client.PostAsJsonAsync($"/api/job-offers/{id}/comments", new { commentId, content = "Only once" }, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(commentId.ToString(), (await ParseJsonAsync(first)).GetProperty("id").GetString());
        Assert.Equal(commentId.ToString(), (await ParseJsonAsync(second)).GetProperty("id").GetString());

        var comments = (await ParseJsonAsync(await client.GetAsync($"/api/job-offers/{id}/comments", Ct))).GetProperty("comments");
        Assert.Equal(1, comments.GetArrayLength());
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
        Assert.True(ownerList.GetProperty("pagination").GetProperty("totalCount").GetInt32() >= 1);
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
        Assert.True(json.GetProperty("pagination").GetProperty("totalCount").GetInt32() >= 1);
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);

        var first = items[0];
        AssertValidGuid(first, "id");
        Assert.NotNull(first.GetProperty("companyName").GetString());
        Assert.NotNull(first.GetProperty("jobTitle").GetString());
        Assert.NotNull(first.GetProperty("status").GetString());
        AssertValidTimestamp(first, "createdAt");
    }

    [Fact]
    public async Task ListMine_ReturnsPaginationMetadata()
    {
        await CreateOfferAs("paging@test.com");

        var response = await client.GetAsync("/api/job-offers/mine?page=1&pageSize=5", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        var pagination = json.GetProperty("pagination");
        Assert.Equal(1, pagination.GetProperty("page").GetInt32());
        Assert.Equal(5, pagination.GetProperty("pageSize").GetInt32());
        Assert.True(pagination.GetProperty("pageCount").GetInt32() >= 1);
        Assert.True(pagination.TryGetProperty("hasNextPage", out _));
        Assert.True(pagination.TryGetProperty("hasPreviousPage", out _));
        Assert.False(pagination.GetProperty("hasPreviousPage").GetBoolean());
    }

    [Fact]
    public async Task ListAll_AsAdmin_ReturnsPaginationMetadata()
    {
        await CreateOfferAs("adminpaging@test.com");

        AuthenticateAs(AdminUserId, "admin@test.com", isAdmin: true);
        var response = await client.GetAsync("/api/job-offers?page=1&pageSize=5", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseJsonAsync(response);
        var pagination = json.GetProperty("pagination");
        Assert.Equal(1, pagination.GetProperty("page").GetInt32());
        Assert.Equal(5, pagination.GetProperty("pageSize").GetInt32());
        Assert.True(pagination.GetProperty("pageCount").GetInt32() >= 1);
        Assert.True(pagination.TryGetProperty("hasNextPage", out _));
        Assert.True(pagination.TryGetProperty("hasPreviousPage", out _));
        Assert.False(pagination.GetProperty("hasPreviousPage").GetBoolean());
    }

    [Fact]
    public async Task ListMine_PageSizeLimitsResults()
    {
        // Create 2 offers for the same user
        var userId = Authenticate("pagelimit@test.com");
        await client.PostAsync("/api/job-offers", CreateValidFormContent(), Ct);
        await client.PostAsync("/api/job-offers", CreateValidFormContent(), Ct);

        var page1Res = await client.GetAsync("/api/job-offers/mine?page=1&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, page1Res.StatusCode);
        var page1 = await ParseJsonAsync(page1Res);
        Assert.Equal(1, page1.GetProperty("items").GetArrayLength());
        Assert.True(page1.GetProperty("pagination").GetProperty("totalCount").GetInt32() >= 2);
        Assert.True(page1.GetProperty("pagination").GetProperty("hasNextPage").GetBoolean());

        var page2Res = await client.GetAsync("/api/job-offers/mine?page=2&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, page2Res.StatusCode);
        var page2 = await ParseJsonAsync(page2Res);
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());
        Assert.True(page2.GetProperty("pagination").GetProperty("hasPreviousPage").GetBoolean());
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

    // ───── Notification emails (via the Temporal workflows) ─────

    [Fact]
    public async Task Create_NotifiesTheOwner()
    {
        var marker = $"Owner should hear about this offer {Guid.NewGuid():N}";
        Authenticate("submitter@test.com");
        var content = CreateValidFormContent(overrides: new() { ["Description"] = marker });

        var response = await client.PostAsync("/api/job-offers", content, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var email = Assert.Single(await WaitForEmailsAsync(m => m.TextBody.Value.Contains(marker), expectedCount: 1));
        Assert.Equal("owner@kalandra.local", email.To.Address);
        Assert.Equal("New job offer: Senior Developer at Acme Corp", email.Subject.Value);
        Assert.Contains("john@acme.com", email.TextBody.Value);
        Assert.Contains("https://www.kalandra.tech/admin/job-offers", email.TextBody.Value);
    }

    [Fact]
    public async Task OfferAuthorsComment_NotifiesOnlyTheOwner()
    {
        var (id, _) = await CreateOfferAs("commenting-author@test.com");

        var marker = $"Author asking for an update {Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync($"/api/job-offers/{id}/comments", new { content = marker }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var emails = await WaitForEmailsAsync(m => m.TextBody.Value.Contains(marker), expectedCount: 1);
        // Grace period: a wrong extra notification would arrive moments later.
        await Task.Delay(1500, Ct);
        emails = [.. factory.EmailSender.Sent.Where(m => m.TextBody.Value.Contains(marker))];

        var email = Assert.Single(emails);
        Assert.Equal("owner@kalandra.local", email.To.Address);
        Assert.StartsWith("New comment on job offer", email.Subject.Value);
        Assert.Contains("https://www.kalandra.tech/admin/job-offers", email.TextBody.Value);
    }

    [Fact]
    public async Task OwnersComment_NotifiesOnlyTheOfferAuthor()
    {
        var (id, _) = await CreateOfferAs("notified-author@test.com");

        AuthenticateAs(AdminUserId, "owner@kalandra.local", isAdmin: true);
        var marker = $"Owner replying to the offer {Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync($"/api/job-offers/{id}/comments", new { content = marker }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var emails = await WaitForEmailsAsync(m => m.TextBody.Value.Contains(marker), expectedCount: 1);
        // Grace period: a wrong extra notification would arrive moments later.
        await Task.Delay(1500, Ct);
        emails = [.. factory.EmailSender.Sent.Where(m => m.TextBody.Value.Contains(marker))];

        var email = Assert.Single(emails);
        Assert.Equal("notified-author@test.com", email.To.Address);
        Assert.StartsWith("New comment on your job offer", email.Subject.Value);
        Assert.Contains("https://www.kalandra.tech/job-offers", email.TextBody.Value);
    }

    [Fact]
    public async Task NonOwnerAdminsComment_NotifiesOwnerAndOfferAuthor()
    {
        var (id, _) = await CreateOfferAs("watched-author@test.com");

        AuthenticateAs(AdminUserId, "second-admin@test.com", isAdmin: true);
        var marker = $"Second admin chiming in {Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync($"/api/job-offers/{id}/comments", new { content = marker }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var emails = await WaitForEmailsAsync(m => m.TextBody.Value.Contains(marker), expectedCount: 2);
        Assert.Equal(2, emails.Length);
        Assert.Contains(emails, m => m.To.Address == "owner@kalandra.local");
        Assert.Contains(emails, m => m.To.Address == "watched-author@test.com");
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

    private async Task<Kalandra.Infrastructure.Email.EmailMessage[]> WaitForEmailsAsync(
        Func<Kalandra.Infrastructure.Email.EmailMessage, bool> predicate, int expectedCount)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var matches = factory.EmailSender.Sent.Where(predicate).ToArray();
            if (matches.Length >= expectedCount)
                return matches;
            await Task.Delay(200, Ct);
        }
        return [.. factory.EmailSender.Sent.Where(predicate)];
    }

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

    private static void AssertValidationFieldError(JsonElement problem, string fieldName)
    {
        var errors = problem.GetProperty("errors");
        Assert.True(
            errors.TryGetProperty(fieldName, out var fieldErrors),
            $"Expected validation error for field '{fieldName}'. Available: {string.Join(", ", errors.EnumerateObject().Select(e => e.Name))}");
        Assert.True(fieldErrors.GetArrayLength() > 0);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(), cancellationToken: CancellationToken.None);
        return doc.RootElement;
    }

    private static MultipartFormDataContent CreateValidFormContent(Dictionary<string, string>? overrides = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["cf-turnstile-response"] = "test-token",
            ["CompanyName"] = "Acme Corp",
            ["ContactName"] = "John Doe",
            ["ContactEmail"] = "john@acme.com",
            ["JobTitle"] = "Senior Developer",
            ["Description"] = "We are looking for a senior developer to join our team.",
            ["SalaryRange"] = "$120k - $160k",
            ["Location"] = "Prague, CZ",
            ["IsRemote"] = "true",
        };

        if (overrides != null)
        {
            foreach (var (k, v) in overrides) fields[k] = v;
        }

        var content = new MultipartFormDataContent();
        foreach (var (k, v) in fields)
            content.Add(new StringContent(v), k);
        return content;
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
