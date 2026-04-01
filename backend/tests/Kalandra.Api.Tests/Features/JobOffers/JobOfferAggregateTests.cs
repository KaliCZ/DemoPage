using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;

namespace Kalandra.Api.Tests.Features.JobOffers;

public class JobOfferAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    private static JobOffer CreateSubmittedOffer(string userId = "owner")
    {
        var offer = new JobOffer();
        offer.Apply(new JobOfferSubmitted(
            UserId: userId,
            UserEmail: $"{userId}@test.com",
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
        var (success, _, evt) = offer.Edit(
            userId: "owner",
            userEmail: "owner@test.com",
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

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal("NewCo", evt!.CompanyName);
    }

    [Fact]
    public void Edit_ByNonOwner_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.Edit(
            userId: "other",
            userEmail: "other@test.com",
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

        Assert.False(success);
        Assert.Equal("Not authorized", error);
    }

    [Fact]
    public void Edit_WhenNotSubmitted_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled(
            CancelledByUserId: "owner",
            CancelledByEmail: "owner@test.com",
            Reason: null,
            Timestamp: Now));

        var (success, error, _) = offer.Edit(
            userId: "owner",
            userEmail: "owner@test.com",
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

        Assert.False(success);
        Assert.Contains("Submitted", error);
    }

    // --- Cancel ---

    [Fact]
    public void Cancel_ByOwner_WhenSubmitted_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, evt) = offer.Cancel(
            userId: "owner",
            userEmail: "owner@test.com",
            reason: "Changed mind",
            timestamp: Now);

        Assert.True(success);
        Assert.NotNull(evt);
    }

    [Fact]
    public void Cancel_ByOwner_WhenInReview_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: "admin",
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.InReview,
            Notes: null,
            Timestamp: Now));

        var (success, _, _) = offer.Cancel(
            userId: "owner", userEmail: "owner@test.com", reason: null, timestamp: Now);
        Assert.True(success);
    }

    [Fact]
    public void Cancel_ByNonOwner_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.Cancel(
            userId: "other", userEmail: "other@test.com", reason: null, timestamp: Now);

        Assert.False(success);
        Assert.Equal("Not authorized", error);
    }

    [Fact]
    public void Cancel_WhenAccepted_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: "admin",
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.Accepted,
            Notes: null,
            Timestamp: Now));

        var (success, _, _) = offer.Cancel(
            userId: "owner", userEmail: "owner@test.com", reason: null, timestamp: Now);
        Assert.False(success);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled(
            CancelledByUserId: "owner",
            CancelledByEmail: "owner@test.com",
            Reason: null,
            Timestamp: Now));

        var (success, _, _) = offer.Cancel(
            userId: "owner", userEmail: "owner@test.com", reason: null, timestamp: Now);
        Assert.False(success);
    }

    // --- ChangeStatus ---

    [Fact]
    public void ChangeStatus_Submitted_To_InReview_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, evt) = offer.ChangeStatus(
            newStatus: JobOfferStatus.InReview,
            changedByUserId: "admin",
            changedByEmail: "admin@test.com",
            notes: null,
            timestamp: Now);

        Assert.True(success);
        Assert.Equal(JobOfferStatus.Submitted, evt!.OldStatus);
        Assert.Equal(JobOfferStatus.InReview, evt.NewStatus);
    }

    [Fact]
    public void ChangeStatus_Submitted_To_Accepted_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, _) = offer.ChangeStatus(
            newStatus: JobOfferStatus.Accepted,
            changedByUserId: "admin",
            changedByEmail: "admin@test.com",
            notes: null,
            timestamp: Now);
        Assert.True(success);
    }

    [Fact]
    public void ChangeStatus_Submitted_To_Declined_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, _) = offer.ChangeStatus(
            newStatus: JobOfferStatus.Declined,
            changedByUserId: "admin",
            changedByEmail: "admin@test.com",
            notes: null,
            timestamp: Now);
        Assert.True(success);
    }

    [Fact]
    public void ChangeStatus_Submitted_To_Cancelled_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.ChangeStatus(
            newStatus: JobOfferStatus.Cancelled,
            changedByUserId: "admin",
            changedByEmail: "admin@test.com",
            notes: null,
            timestamp: Now);

        Assert.False(success);
        Assert.Contains("Cannot change status", error);
    }

    [Fact]
    public void ChangeStatus_InReview_To_Accepted_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: "admin",
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.InReview,
            Notes: null,
            Timestamp: Now));

        var (success, _, _) = offer.ChangeStatus(
            newStatus: JobOfferStatus.Accepted,
            changedByUserId: "admin",
            changedByEmail: "admin@test.com",
            notes: null,
            timestamp: Now);
        Assert.True(success);
    }

    [Fact]
    public void ChangeStatus_InReview_To_Declined_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: "admin",
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.InReview,
            Notes: null,
            Timestamp: Now));

        var (success, _, _) = offer.ChangeStatus(
            newStatus: JobOfferStatus.Declined,
            changedByUserId: "admin",
            changedByEmail: "admin@test.com",
            notes: null,
            timestamp: Now);
        Assert.True(success);
    }

    [Fact]
    public void ChangeStatus_Accepted_To_Anything_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: "admin",
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.Accepted,
            Notes: null,
            Timestamp: Now));

        var (success, _, _) = offer.ChangeStatus(
            newStatus: JobOfferStatus.InReview,
            changedByUserId: "admin",
            changedByEmail: "admin@test.com",
            notes: null,
            timestamp: Now);
        Assert.False(success);
    }

    [Fact]
    public void ChangeStatus_Declined_To_Anything_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: "admin",
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.Declined,
            Notes: null,
            Timestamp: Now));

        var (success, _, _) = offer.ChangeStatus(
            newStatus: JobOfferStatus.Submitted,
            changedByUserId: "admin",
            changedByEmail: "admin@test.com",
            notes: null,
            timestamp: Now);
        Assert.False(success);
    }

    [Fact]
    public void ChangeStatus_Cancelled_To_Anything_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled(
            CancelledByUserId: "owner",
            CancelledByEmail: "owner@test.com",
            Reason: null,
            Timestamp: Now));

        var (success, _, _) = offer.ChangeStatus(
            newStatus: JobOfferStatus.Submitted,
            changedByUserId: "admin",
            changedByEmail: "admin@test.com",
            notes: null,
            timestamp: Now);
        Assert.False(success);
    }

    [Fact]
    public void ChangeStatus_ToSameStatus_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.ChangeStatus(
            newStatus: JobOfferStatus.Submitted,
            changedByUserId: "admin",
            changedByEmail: "admin@test.com",
            notes: null,
            timestamp: Now);

        Assert.False(success);
        Assert.Contains("already in status", error);
    }

    // --- AddComment ---

    [Fact]
    public void AddComment_ByOwner_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, evt) = offer.AddComment(
            userId: "owner",
            userEmail: "owner@test.com",
            userName: "Owner",
            content: "Hello",
            isAdmin: false,
            timestamp: Now);

        Assert.True(success);
        Assert.Equal("Hello", evt!.Content);
    }

    [Fact]
    public void AddComment_ByAdmin_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, _) = offer.AddComment(
            userId: "admin",
            userEmail: "admin@test.com",
            userName: "Admin",
            content: "Reply",
            isAdmin: true,
            timestamp: Now);
        Assert.True(success);
    }

    [Fact]
    public void AddComment_ByNonOwnerNonAdmin_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.AddComment(
            userId: "other",
            userEmail: "other@test.com",
            userName: "Other",
            content: "Hi",
            isAdmin: false,
            timestamp: Now);

        Assert.False(success);
        Assert.Equal("Not authorized", error);
    }

    [Fact]
    public void AddComment_EmptyContent_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.AddComment(
            userId: "owner",
            userEmail: "owner@test.com",
            userName: "Owner",
            content: "   ",
            isAdmin: false,
            timestamp: Now);

        Assert.False(success);
        Assert.Equal("Content is required", error);
    }

    // --- Apply ---

    [Fact]
    public void Apply_Submitted_SetsInitialState()
    {
        var offer = new JobOffer();
        offer.Apply(new JobOfferSubmitted(
            UserId: "u1",
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

        Assert.Equal("u1", offer.UserId);
        Assert.Equal("Co", offer.CompanyName);
        Assert.Equal(JobOfferStatus.Submitted, offer.Status);
        Assert.Equal(Now, offer.CreatedAt);
    }

    [Fact]
    public void Apply_Edited_UpdatesFields()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferEdited(
            EditedByUserId: "owner",
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
    public void Apply_StatusChanged_UpdatesStatus()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged(
            ChangedByUserId: "admin",
            ChangedByEmail: "admin@test.com",
            OldStatus: JobOfferStatus.Submitted,
            NewStatus: JobOfferStatus.InReview,
            Notes: "Reviewing",
            Timestamp: Now));

        Assert.Equal(JobOfferStatus.InReview, offer.Status);
        Assert.Equal("Reviewing", offer.AdminNotes);
    }

    [Fact]
    public void Apply_Cancelled_SetsStatusCancelled()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled(
            CancelledByUserId: "owner",
            CancelledByEmail: "owner@test.com",
            Reason: "Reason",
            Timestamp: Now));

        Assert.Equal(JobOfferStatus.Cancelled, offer.Status);
    }
}
