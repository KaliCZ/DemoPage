using Kalandra.JobOffers.Entities;

namespace Kalandra.JobOffers.Events;

public record JobOfferSubmitted(
    Guid UserId,
    NonEmptyString UserEmail,
    NonEmptyString CompanyName,
    NonEmptyString ContactName,
    NonEmptyString ContactEmail,
    NonEmptyString JobTitle,
    NonEmptyString Description,
    string? SalaryRange,
    string? Location,
    bool IsRemote,
    string? AdditionalNotes,
    IReadOnlyList<AttachmentInfo> Attachments,
    DateTimeOffset Timestamp);
