using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Events;

namespace Kalandra.JobOffers.Entities;

public enum EditJobOfferError { NotFound, NotAuthorized, NotSubmittedStatus }
public enum CancelJobOfferError { NotFound, NotAuthorized, InvalidStatus }
public enum UpdateJobOfferStatusError { NotFound, InvalidTransition }
public enum AddCommentError { NotFound, NotAuthorized }

/// <summary>
/// Marten event-sourced aggregate for job offers.
/// State is rebuilt by applying events in order.
/// </summary>
public class JobOffer
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Try<JobOfferEdited, EditJobOfferError> Edit(
        CurrentUser user,
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
        if (UserId != user.Id)
            return Try.Error<JobOfferEdited, EditJobOfferError>(EditJobOfferError.NotAuthorized);

        if (Status != JobOfferStatus.Submitted)
            return Try.Error<JobOfferEdited, EditJobOfferError>(EditJobOfferError.NotSubmittedStatus);

        // Emit only the fields that actually changed. Null on the event means
        // "not edited" so unchanged fields stay null and are skipped by both
        // Apply() and the activity-log projection.
        //
        // Clearing a previously-set optional field is NOT supported: when the
        // caller passes null for SalaryRange / Location / AdditionalNotes we
        // treat it as "leave this field alone" rather than "set it to null".
        // The frontend is expected to mirror this — see the edit form in
        // frontend/src/pages/[...lang]/job-offers.astro.
        return Try.Success<JobOfferEdited, EditJobOfferError>(new JobOfferEdited(
            EditedByUserId: user.Id,
            EditedByEmail: user.Email.Address,
            CompanyName: companyName != CompanyName ? companyName : null,
            ContactName: contactName != ContactName ? contactName : null,
            ContactEmail: contactEmail != ContactEmail ? contactEmail : null,
            JobTitle: jobTitle != JobTitle ? jobTitle : null,
            Description: description != Description ? description : null,
            SalaryRange: salaryRange is not null && salaryRange != SalaryRange ? salaryRange : null,
            Location: location is not null && location != Location ? location : null,
            IsRemote: isRemote != IsRemote ? isRemote : null,
            AdditionalNotes: additionalNotes is not null && additionalNotes != AdditionalNotes ? additionalNotes : null,
            Timestamp: timestamp));
    }

    public Try<JobOfferCancelled, CancelJobOfferError> Cancel(
        CurrentUser user,
        string? reason,
        DateTimeOffset timestamp)
    {
        if (UserId != user.Id)
            return Try.Error<JobOfferCancelled, CancelJobOfferError>(CancelJobOfferError.NotAuthorized);

        if (Status is not (JobOfferStatus.Submitted or JobOfferStatus.InReview))
            return Try.Error<JobOfferCancelled, CancelJobOfferError>(CancelJobOfferError.InvalidStatus);

        return Try.Success<JobOfferCancelled, CancelJobOfferError>(new JobOfferCancelled(
            CancelledByUserId: user.Id,
            CancelledByEmail: user.Email.Address,
            Reason: reason,
            Timestamp: timestamp));
    }

    public Try<JobOfferStatusChanged, UpdateJobOfferStatusError> ChangeStatus(
        JobOfferStatus newStatus,
        CurrentUser user,
        string? notes,
        DateTimeOffset timestamp)
    {
        if (newStatus == Status || !CanTransitionTo(newStatus))
            return Try.Error<JobOfferStatusChanged, UpdateJobOfferStatusError>(UpdateJobOfferStatusError.InvalidTransition);

        return Try.Success<JobOfferStatusChanged, UpdateJobOfferStatusError>(new JobOfferStatusChanged(
            ChangedByUserId: user.Id,
            ChangedByEmail: user.Email.Address,
            OldStatus: Status,
            NewStatus: newStatus,
            Notes: notes,
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
        UpdatedAt = e.Timestamp;
    }

    public void Apply(JobOfferEdited e)
    {
        // Null means "not edited" — preserve the current value for that field.
        if (e.CompanyName != null) CompanyName = e.CompanyName;
        if (e.ContactName != null) ContactName = e.ContactName;
        if (e.ContactEmail != null) ContactEmail = e.ContactEmail;
        if (e.JobTitle != null) JobTitle = e.JobTitle;
        if (e.Description != null) Description = e.Description;
        if (e.SalaryRange != null) SalaryRange = e.SalaryRange;
        if (e.Location != null) Location = e.Location;
        if (e.IsRemote.HasValue) IsRemote = e.IsRemote.Value;
        if (e.AdditionalNotes != null) AdditionalNotes = e.AdditionalNotes;
        UpdatedAt = e.Timestamp;
    }

    public void Apply(JobOfferCancelled e)
    {
        Status = JobOfferStatus.Cancelled;
        UpdatedAt = e.Timestamp;
    }

    private bool CanTransitionTo(JobOfferStatus newStatus) => Status switch
    {
        JobOfferStatus.Submitted => newStatus is JobOfferStatus.InReview
            or JobOfferStatus.LetsTalk
            or JobOfferStatus.Declined,
        JobOfferStatus.InReview => newStatus is JobOfferStatus.LetsTalk
            or JobOfferStatus.Declined,
        JobOfferStatus.LetsTalk => false,
        JobOfferStatus.Declined => false,
        JobOfferStatus.Cancelled => false,
    };
}
