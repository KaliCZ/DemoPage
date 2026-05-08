using Kalandra.JobOffers.Entities;
using StrongTypes;

namespace Kalandra.JobOffers.Events;

public record JobOfferSubmitted(
    Guid UserId,
    Email UserEmail,
    NonEmptyString CompanyName,
    NonEmptyString ContactName,
    Email ContactEmail,
    NonEmptyString JobTitle,
    NonEmptyString Description,
    string? SalaryRange,
    string? Location,
    bool IsRemote,
    string? AdditionalNotes,
    IReadOnlyList<AttachmentInfo> Attachments,
    DateTimeOffset Timestamp);
