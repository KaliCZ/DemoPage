using Kalandra.Api.Features.JobOffers.Events;

namespace Kalandra.Api.Features.JobOffers.Entities;

/// <summary>
/// Marten event-sourced aggregate for job offers.
/// State is rebuilt by applying events in order.
/// </summary>
public class JobOffer
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? SalaryRange { get; set; }
    public string? Location { get; set; }
    public bool IsRemote { get; set; }
    public string? AdditionalNotes { get; set; }
    public IReadOnlyList<AttachmentInfo> Attachments { get; set; } = [];

    public JobOfferStatus Status { get; set; } = JobOfferStatus.Submitted;
    public string? AdminNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public (bool Success, string? Error, JobOfferEdited? Event) Edit(
        string userId,
        string userEmail,
        string companyName,
        string contactName,
        string contactEmail,
        string jobTitle,
        string description,
        string? salaryRange,
        string? location,
        bool isRemote,
        string? additionalNotes,
        DateTimeOffset timestamp)
    {
        if (UserId != userId)
            return (false, "Not authorized", null);

        if (Status != JobOfferStatus.Submitted)
            return (false, "Can only edit offers with status Submitted", null);

        return (true, null, new JobOfferEdited(
            EditedByUserId: userId,
            EditedByEmail: userEmail,
            CompanyName: companyName,
            ContactName: contactName,
            ContactEmail: contactEmail,
            JobTitle: jobTitle,
            Description: description,
            SalaryRange: salaryRange,
            Location: location,
            IsRemote: isRemote,
            AdditionalNotes: additionalNotes,
            Timestamp: timestamp));
    }

    public (bool Success, string? Error, JobOfferCancelled? Event) Cancel(
        string userId,
        string userEmail,
        string? reason,
        DateTimeOffset timestamp)
    {
        if (UserId != userId)
            return (false, "Not authorized", null);

        if (Status is not (JobOfferStatus.Submitted or JobOfferStatus.InReview))
            return (false, "Cannot cancel an offer that has already been accepted, declined, or cancelled", null);

        return (true, null, new JobOfferCancelled(
            CancelledByUserId: userId,
            CancelledByEmail: userEmail,
            Reason: reason,
            Timestamp: timestamp));
    }

    public (bool Success, string? Error, JobOfferStatusChanged? Event) ChangeStatus(
        JobOfferStatus newStatus,
        string changedByUserId,
        string changedByEmail,
        string? notes,
        DateTimeOffset timestamp)
    {
        if (newStatus == Status)
            return (false, $"Job offer is already in status {Status}", null);

        if (!CanTransitionTo(newStatus))
            return (false, $"Cannot change status from {Status} to {newStatus}", null);

        return (true, null, new JobOfferStatusChanged(
            ChangedByUserId: changedByUserId,
            ChangedByEmail: changedByEmail,
            OldStatus: Status,
            NewStatus: newStatus,
            Notes: notes,
            Timestamp: timestamp));
    }

    public (bool Success, string? Error, JobOfferCommentAdded? Event) AddComment(
        string userId,
        string userEmail,
        string userName,
        string content,
        bool isAdmin,
        DateTimeOffset timestamp)
    {
        if (!isAdmin && UserId != userId)
            return (false, "Not authorized", null);

        if (string.IsNullOrWhiteSpace(content))
            return (false, "Content is required", null);

        return (true, null, new JobOfferCommentAdded(
            CommentId: Guid.NewGuid(),
            UserId: userId,
            UserEmail: userEmail,
            UserName: userName,
            Content: content.Trim(),
            Timestamp: timestamp));
    }

    public void Apply(JobOfferSubmitted e)
    {
        UserId = e.UserId;
        UserEmail = e.UserEmail;
        CompanyName = e.CompanyName;
        ContactName = e.ContactName;
        ContactEmail = e.ContactEmail;
        JobTitle = e.JobTitle;
        Description = e.Description;
        SalaryRange = e.SalaryRange;
        Location = e.Location;
        IsRemote = e.IsRemote;
        AdditionalNotes = e.AdditionalNotes;
        Attachments = e.Attachments;
        Status = JobOfferStatus.Submitted;
        CreatedAt = e.Timestamp;
        UpdatedAt = e.Timestamp;
    }

    public void Apply(JobOfferStatusChanged e)
    {
        Status = e.NewStatus;
        AdminNotes = e.Notes ?? AdminNotes;
        UpdatedAt = e.Timestamp;
    }

    public void Apply(JobOfferEdited e)
    {
        CompanyName = e.CompanyName;
        ContactName = e.ContactName;
        ContactEmail = e.ContactEmail;
        JobTitle = e.JobTitle;
        Description = e.Description;
        SalaryRange = e.SalaryRange;
        Location = e.Location;
        IsRemote = e.IsRemote;
        AdditionalNotes = e.AdditionalNotes;
        UpdatedAt = e.Timestamp;
    }

    public void Apply(JobOfferCancelled e)
    {
        Status = JobOfferStatus.Cancelled;
        UpdatedAt = e.Timestamp;
    }

    // Comments don't change aggregate state — they're read from the event stream directly
    public void Apply(JobOfferCommentAdded _) { }

    private bool CanTransitionTo(JobOfferStatus newStatus) => Status switch
    {
        JobOfferStatus.Submitted => newStatus is JobOfferStatus.InReview
            or JobOfferStatus.Accepted
            or JobOfferStatus.Declined,
        JobOfferStatus.InReview => newStatus is JobOfferStatus.Accepted
            or JobOfferStatus.Declined,
        JobOfferStatus.Accepted => false,
        JobOfferStatus.Declined => false,
        JobOfferStatus.Cancelled => false,
        _ => false
    };
}
