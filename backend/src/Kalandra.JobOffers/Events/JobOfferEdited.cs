using StrongTypes;

namespace Kalandra.JobOffers.Events;

/// <summary>A null field was not changed by this edit.</summary>
public record JobOfferEdited(
    Guid EditedByUserId,
    Email EditedByEmail,
    NonEmptyString? CompanyName,
    NonEmptyString? ContactName,
    Email? ContactEmail,
    NonEmptyString? JobTitle,
    NonEmptyString? Description,
    string? SalaryRange,
    string? Location,
    bool? IsRemote,
    string? AdditionalNotes,
    DateTimeOffset Timestamp);
