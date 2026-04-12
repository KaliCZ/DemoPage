using System.Net.Mail;
using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;

namespace Kalandra.JobOffers.Tests;

public class JobOfferAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid OwnerId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AdminId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly CurrentUser Owner = new(OwnerId, new MailAddress("owner@test.com"), "Owner", []);
    private static readonly CurrentUser Other = new(OtherId, new MailAddress("other@test.com"), "Other", []);
    private static readonly CurrentUser Admin = new(AdminId, new MailAddress("admin@test.com"), "Admin", [UserRole.Admin]);

    private static JobOffer CreateSubmittedOffer(Guid? userId = null)
    {
        var id = userId ?? OwnerId;
        var offer = new JobOffer();
        offer.Apply(new JobOfferSubmitted(
            UserId: id,
            UserEmail: $"{id}@test.com",
            CompanyName: "Acme",
            ContactName: "John",
            ContactEmail: "john@acme.com",
            JobTitle: "Dev",
            Description: "Desc",
            SalaryRange: null,
            Location: "Prague",
            IsRemote: true,
            AdditionalNotes: null,
            Attachments: [],
            Timestamp: Now));
        return offer;
    }

    // --- Edit ---

    [Fact]
    public void Edit_ByOwner_WhenSubmitted_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var result = offer.Edit(
            user: Owner,
            companyName: "NewCo",
            contactName: "Jane",
            contactEmail: "jane@co.com",
            jobTitle: "CTO",
            description: "New desc",
            salaryRange: null,
            location: null,
            isRemote: true,
            additionalNotes: null,
            timestamp: Now);

        Assert.True(result.IsSuccess);
        var evt = result.Success.Get();
        Assert.Equal("NewCo", evt.CompanyName);
    }

    [Fact]
    public void Edit_ByNonOwner_Fails()
    {
        var offer = CreateSubmittedOffer();
        var result = offer.Edit(
            user: Other,
            companyName: "Co",
            contactName: "J",
            contactEmail: "j@co.com",
            jobTitle: "Dev",
            description: "Desc",
            salaryRange: null,
            location: null,
            isRemote: false,
            additionalNotes: null,
            timestamp: Now);

        Assert.True(result.IsError);
        Assert.Equal(EditJobOfferError.NotAuthorized, result.Error.Get());
    }

    [Fact]
    public void Edit_WhenNotSubmitted_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled(
            CancelledByUserId: OwnerId,
            CancelledByEmail: "owner@test.com",
            Reason: null,
            Timestamp: Now));

        var result = offer.Edit(
            user: Owner,
            companyName: "Co",
            contactName: "J",
            contactEmail: "j@co.com",
            jobTitle: "Dev",
            description: "Desc",
            salaryRange: null,
            location: null,
            isRemote: false,
            additionalNotes: null,
            timestamp: Now);

        Assert.True(result.IsError);
        Assert.Equal(EditJobOfferError.NotSubmittedStatus, result.Error.Get());
    }

    // --- Cancel ---

    [Fact]
    public void Cancel_ByOwner_WhenSubmitted_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var result = offer.Cancel(
            user: Owner,
            reason: "Changed mind",
            timestamp: Now);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Cancel_ByOwner_WhenInReview_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: AdminId,
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.InReview,
            Notes: null,
            Timestamp: Now));

        var result = offer.Cancel(user: Owner, reason: null, timestamp: Now);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Cancel_ByNonOwner_Fails()
    {
        var offer = CreateSubmittedOffer();
        var result = offer.Cancel(user: Other, reason: null, timestamp: Now);

        Assert.True(result.IsError);
        Assert.Equal(CancelJobOfferError.NotAuthorized, result.Error.Get());
    }

    [Fact]
    public void Cancel_WhenLetsTalk_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: AdminId,
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.LetsTalk,
            Notes: null,
            Timestamp: Now));

        var result = offer.Cancel(user: Owner, reason: null, timestamp: Now);
        Assert.True(result.IsError);
        Assert.Equal(CancelJobOfferError.InvalidStatus, result.Error.Get());
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled(
            CancelledByUserId: OwnerId,
            CancelledByEmail: "owner@test.com",
            Reason: null,
            Timestamp: Now));

        var result = offer.Cancel(user: Owner, reason: null, timestamp: Now);
        Assert.True(result.IsError);
        Assert.Equal(CancelJobOfferError.InvalidStatus, result.Error.Get());
    }

    // --- ChangeStatus ---

    [Fact]
    public void ChangeStatus_Submitted_To_InReview_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var result = offer.ChangeStatus(
            newStatus: JobOfferStatus.InReview,
            user: Admin,
            notes: null,
            timestamp: Now);

        Assert.True(result.IsSuccess);
        var evt = result.Success.Get();
        Assert.Equal(JobOfferStatus.Submitted, evt.OldStatus);
        Assert.Equal(JobOfferStatus.InReview, evt.NewStatus);
    }

    [Fact]
    public void ChangeStatus_Submitted_To_LetsTalk_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var result = offer.ChangeStatus(
            newStatus: JobOfferStatus.LetsTalk,
            user: Admin,
            notes: null,
            timestamp: Now);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ChangeStatus_Submitted_To_Declined_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var result = offer.ChangeStatus(
            newStatus: JobOfferStatus.Declined,
            user: Admin,
            notes: null,
            timestamp: Now);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ChangeStatus_Submitted_To_Cancelled_Fails()
    {
        var offer = CreateSubmittedOffer();
        var result = offer.ChangeStatus(
            newStatus: JobOfferStatus.Cancelled,
            user: Admin,
            notes: null,
            timestamp: Now);

        Assert.True(result.IsError);
        Assert.Equal(
            UpdateJobOfferStatusError.InvalidTransition,
            result.Error.Get());
    }

    [Fact]
    public void ChangeStatus_InReview_To_LetsTalk_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: AdminId,
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.InReview,
            Notes: null,
            Timestamp: Now));

        var result = offer.ChangeStatus(
            newStatus: JobOfferStatus.LetsTalk,
            user: Admin,
            notes: null,
            timestamp: Now);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ChangeStatus_InReview_To_Declined_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: AdminId,
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.InReview,
            Notes: null,
            Timestamp: Now));

        var result = offer.ChangeStatus(
            newStatus: JobOfferStatus.Declined,
            user: Admin,
            notes: null,
            timestamp: Now);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ChangeStatus_LetsTalk_To_Anything_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: AdminId,
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.LetsTalk,
            Notes: null,
            Timestamp: Now));

        var result = offer.ChangeStatus(
            newStatus: JobOfferStatus.InReview,
            user: Admin,
            notes: null,
            timestamp: Now);
        Assert.True(result.IsError);
    }

    [Fact]
    public void ChangeStatus_Declined_To_Anything_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: AdminId,
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.Declined,
            Notes: null,
            Timestamp: Now));

        var result = offer.ChangeStatus(
            newStatus: JobOfferStatus.Submitted,
            user: Admin,
            notes: null,
            timestamp: Now);
        Assert.True(result.IsError);
    }

    [Fact]
    public void ChangeStatus_Cancelled_To_Anything_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled(
            CancelledByUserId: OwnerId,
            CancelledByEmail: "owner@test.com",
            Reason: null,
            Timestamp: Now));

        var result = offer.ChangeStatus(
            newStatus: JobOfferStatus.Submitted,
            user: Admin,
            notes: null,
            timestamp: Now);
        Assert.True(result.IsError);
    }

    [Fact]
    public void ChangeStatus_ToSameStatus_Fails()
    {
        var offer = CreateSubmittedOffer();
        var result = offer.ChangeStatus(
            newStatus: JobOfferStatus.Submitted,
            user: Admin,
            notes: null,
            timestamp: Now);

        Assert.True(result.IsError);
        Assert.Equal(
            UpdateJobOfferStatusError.InvalidTransition,
            result.Error.Get());
    }

    // --- Apply ---

    [Fact]
    public void Apply_Submitted_SetsInitialState()
    {
        var u1 = new Guid("33333333-3333-3333-3333-333333333333");
        var offer = new JobOffer();
        offer.Apply(new JobOfferSubmitted(
            UserId: u1,
            UserEmail: "u1@test.com",
            CompanyName: "Co",
            ContactName: "Name",
            ContactEmail: "c@co.com",
            JobTitle: "Dev",
            Description: "Desc",
            SalaryRange: "$100k",
            Location: "NYC",
            IsRemote: true,
            AdditionalNotes: "Notes",
            Attachments: [],
            Timestamp: Now));

        Assert.Equal(u1, offer.UserId);
        Assert.Equal("Co", offer.CompanyName);
        Assert.Equal(JobOfferStatus.Submitted, offer.Status);
        Assert.Equal(Now, offer.CreatedAt);
    }

    [Fact]
    public void Apply_Edited_UpdatesFields()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferEdited(
            EditedByUserId: OwnerId,
            EditedByEmail: "owner@test.com",
            CompanyName: "NewCo",
            ContactName: "Jane",
            ContactEmail: "jane@co.com",
            JobTitle: "CTO",
            Description: "New",
            SalaryRange: "$200k",
            Location: "Remote",
            IsRemote: false,
            AdditionalNotes: "Updated",
            Timestamp: Now.AddHours(1)));

        Assert.Equal("NewCo", offer.CompanyName);
        Assert.Equal("CTO", offer.JobTitle);
        Assert.Equal(Now.AddHours(1), offer.UpdatedAt);
        Assert.Equal(Now, offer.CreatedAt); // CreatedAt unchanged
    }

    [Fact]
    public void Apply_Edited_NullFields_ArePreserved()
    {
        // Null on a JobOfferEdited field means "not edited" — existing values
        // on the aggregate should be kept intact.
        var offer = CreateSubmittedOffer();
        var originalCompany = offer.CompanyName;
        var originalLocation = offer.Location;
        var originalIsRemote = offer.IsRemote;

        offer.Apply(new JobOfferEdited(
            EditedByUserId: OwnerId,
            EditedByEmail: "owner@test.com",
            CompanyName: null,
            ContactName: null,
            ContactEmail: null,
            JobTitle: "CTO", // the only field actually edited
            Description: null,
            SalaryRange: null,
            Location: null,
            IsRemote: null,
            AdditionalNotes: null,
            Timestamp: Now.AddHours(1)));

        Assert.Equal(originalCompany, offer.CompanyName);
        Assert.Equal("CTO", offer.JobTitle);
        Assert.Equal(originalLocation, offer.Location);
        Assert.Equal(originalIsRemote, offer.IsRemote);
    }

    [Fact]
    public void Edit_OnlyEmitsChangedFields()
    {
        // Edit() compares input against the aggregate's current state and
        // emits null for any field that wasn't actually changed.
        var offer = CreateSubmittedOffer();
        var result = offer.Edit(
            user: Owner,
            companyName: "Acme",           // unchanged
            contactName: "John",           // unchanged
            contactEmail: "john@acme.com", // unchanged
            jobTitle: "Senior Dev",        // changed (was "Dev")
            description: "Desc",           // unchanged
            salaryRange: null,             // unchanged (was null)
            location: "Prague",            // unchanged
            isRemote: true,                // unchanged
            additionalNotes: null,         // unchanged (was null)
            timestamp: Now.AddHours(1));

        Assert.True(result.IsSuccess);
        var evt = result.Success.Get();
        Assert.Null(evt.CompanyName);
        Assert.Null(evt.ContactName);
        Assert.Null(evt.ContactEmail);
        Assert.Equal("Senior Dev", evt.JobTitle);
        Assert.Null(evt.Description);
        Assert.Null(evt.SalaryRange);
        Assert.Null(evt.Location);
        Assert.Null(evt.IsRemote);
        Assert.Null(evt.AdditionalNotes);
    }

    [Fact]
    public void Apply_StatusChanged_UpdatesStatus()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: AdminId,
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.InReview,
            Notes: "Reviewing",
            Timestamp: Now));

        Assert.Equal(JobOfferStatus.InReview, offer.Status);
    }

    [Fact]
    public void Apply_Cancelled_SetsStatusCancelled()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled(
            CancelledByUserId: OwnerId,
            CancelledByEmail: "owner@test.com",
            Reason: "Reason",
            Timestamp: Now));

        Assert.Equal(JobOfferStatus.Cancelled, offer.Status);
    }
}
