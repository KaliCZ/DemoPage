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
        var (success, _, evt) = offer.Edit("owner", "owner@test.com", "NewCo", "Jane", "jane@co.com",
            "CTO", "New desc", null, null, true, null, Now);

        Assert.True(success);
        Assert.NotNull(evt);
        Assert.Equal("NewCo", evt!.CompanyName);
    }

    [Fact]
    public void Edit_ByNonOwner_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.Edit("other", "other@test.com", "Co", "J", "j@co.com",
            "Dev", "Desc", null, null, false, null, Now);

        Assert.False(success);
        Assert.Equal("Not authorized", error);
    }

    [Fact]
    public void Edit_WhenNotSubmitted_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled("owner", "owner@test.com", null, Now));

        var (success, error, _) = offer.Edit("owner", "owner@test.com", "Co", "J", "j@co.com",
            "Dev", "Desc", null, null, false, null, Now);

        Assert.False(success);
        Assert.Contains("Submitted", error);
    }

    // --- Cancel ---

    [Fact]
    public void Cancel_ByOwner_WhenSubmitted_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, evt) = offer.Cancel("owner", "owner@test.com", "Changed mind", Now);

        Assert.True(success);
        Assert.NotNull(evt);
    }

    [Fact]
    public void Cancel_ByOwner_WhenInReview_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged("admin", "admin@test.com",
            JobOfferStatus.Submitted, JobOfferStatus.InReview, null, Now));

        var (success, _, _) = offer.Cancel("owner", "owner@test.com", null, Now);
        Assert.True(success);
    }

    [Fact]
    public void Cancel_ByNonOwner_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.Cancel("other", "other@test.com", null, Now);

        Assert.False(success);
        Assert.Equal("Not authorized", error);
    }

    [Fact]
    public void Cancel_WhenAccepted_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged("admin", "admin@test.com",
            JobOfferStatus.Submitted, JobOfferStatus.Accepted, null, Now));

        var (success, _, _) = offer.Cancel("owner", "owner@test.com", null, Now);
        Assert.False(success);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled("owner", "owner@test.com", null, Now));

        var (success, _, _) = offer.Cancel("owner", "owner@test.com", null, Now);
        Assert.False(success);
    }

    // --- ChangeStatus ---

    [Fact]
    public void ChangeStatus_Submitted_To_InReview_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, evt) = offer.ChangeStatus(JobOfferStatus.InReview, "admin", "admin@test.com", null, Now);

        Assert.True(success);
        Assert.Equal(JobOfferStatus.Submitted, evt!.OldStatus);
        Assert.Equal(JobOfferStatus.InReview, evt.NewStatus);
    }

    [Fact]
    public void ChangeStatus_Submitted_To_Accepted_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, _) = offer.ChangeStatus(JobOfferStatus.Accepted, "admin", "admin@test.com", null, Now);
        Assert.True(success);
    }

    [Fact]
    public void ChangeStatus_Submitted_To_Declined_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, _) = offer.ChangeStatus(JobOfferStatus.Declined, "admin", "admin@test.com", null, Now);
        Assert.True(success);
    }

    [Fact]
    public void ChangeStatus_Submitted_To_Cancelled_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.ChangeStatus(JobOfferStatus.Cancelled, "admin", "admin@test.com", null, Now);

        Assert.False(success);
        Assert.Contains("Cannot change status", error);
    }

    [Fact]
    public void ChangeStatus_InReview_To_Accepted_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged("admin", "admin@test.com",
            JobOfferStatus.Submitted, JobOfferStatus.InReview, null, Now));

        var (success, _, _) = offer.ChangeStatus(JobOfferStatus.Accepted, "admin", "admin@test.com", null, Now);
        Assert.True(success);
    }

    [Fact]
    public void ChangeStatus_InReview_To_Declined_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged("admin", "admin@test.com",
            JobOfferStatus.Submitted, JobOfferStatus.InReview, null, Now));

        var (success, _, _) = offer.ChangeStatus(JobOfferStatus.Declined, "admin", "admin@test.com", null, Now);
        Assert.True(success);
    }

    [Fact]
    public void ChangeStatus_Accepted_To_Anything_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged("admin", "admin@test.com",
            JobOfferStatus.Submitted, JobOfferStatus.Accepted, null, Now));

        var (success, _, _) = offer.ChangeStatus(JobOfferStatus.InReview, "admin", "admin@test.com", null, Now);
        Assert.False(success);
    }

    [Fact]
    public void ChangeStatus_Declined_To_Anything_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged("admin", "admin@test.com",
            JobOfferStatus.Submitted, JobOfferStatus.Declined, null, Now));

        var (success, _, _) = offer.ChangeStatus(JobOfferStatus.Submitted, "admin", "admin@test.com", null, Now);
        Assert.False(success);
    }

    [Fact]
    public void ChangeStatus_Cancelled_To_Anything_Fails()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled("owner", "owner@test.com", null, Now));

        var (success, _, _) = offer.ChangeStatus(JobOfferStatus.Submitted, "admin", "admin@test.com", null, Now);
        Assert.False(success);
    }

    [Fact]
    public void ChangeStatus_ToSameStatus_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.ChangeStatus(JobOfferStatus.Submitted, "admin", "admin@test.com", null, Now);

        Assert.False(success);
        Assert.Contains("already in status", error);
    }

    // --- AddComment ---

    [Fact]
    public void AddComment_ByOwner_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, evt) = offer.AddComment("owner", "owner@test.com", "Owner", "Hello", false, Now);

        Assert.True(success);
        Assert.Equal("Hello", evt!.Content);
    }

    [Fact]
    public void AddComment_ByAdmin_Succeeds()
    {
        var offer = CreateSubmittedOffer();
        var (success, _, _) = offer.AddComment("admin", "admin@test.com", "Admin", "Reply", true, Now);
        Assert.True(success);
    }

    [Fact]
    public void AddComment_ByNonOwnerNonAdmin_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.AddComment("other", "other@test.com", "Other", "Hi", false, Now);

        Assert.False(success);
        Assert.Equal("Not authorized", error);
    }

    [Fact]
    public void AddComment_EmptyContent_Fails()
    {
        var offer = CreateSubmittedOffer();
        var (success, error, _) = offer.AddComment("owner", "owner@test.com", "Owner", "   ", false, Now);

        Assert.False(success);
        Assert.Equal("Content is required", error);
    }

    // --- Apply ---

    [Fact]
    public void Apply_Submitted_SetsInitialState()
    {
        var offer = new JobOffer();
        offer.Apply(new JobOfferSubmitted("u1", "u1@test.com", "Co", "Name", "c@co.com",
            "Dev", "Desc", "$100k", "NYC", true, "Notes", [], Now));

        Assert.Equal("u1", offer.UserId);
        Assert.Equal("Co", offer.CompanyName);
        Assert.Equal(JobOfferStatus.Submitted, offer.Status);
        Assert.Equal(Now, offer.CreatedAt);
    }

    [Fact]
    public void Apply_Edited_UpdatesFields()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferEdited("owner", "owner@test.com", "NewCo", "Jane", "jane@co.com",
            "CTO", "New", "$200k", "Remote", false, "Updated", Now.AddHours(1)));

        Assert.Equal("NewCo", offer.CompanyName);
        Assert.Equal("CTO", offer.JobTitle);
        Assert.Equal(Now.AddHours(1), offer.UpdatedAt);
        Assert.Equal(Now, offer.CreatedAt); // CreatedAt unchanged
    }

    [Fact]
    public void Apply_StatusChanged_UpdatesStatus()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferStatusChanged("admin", "admin@test.com",
            JobOfferStatus.Submitted, JobOfferStatus.InReview, "Reviewing", Now));

        Assert.Equal(JobOfferStatus.InReview, offer.Status);
        Assert.Equal("Reviewing", offer.AdminNotes);
    }

    [Fact]
    public void Apply_Cancelled_SetsStatusCancelled()
    {
        var offer = CreateSubmittedOffer();
        offer.Apply(new JobOfferCancelled("owner", "owner@test.com", "Reason", Now));

        Assert.Equal(JobOfferStatus.Cancelled, offer.Status);
    }
}
