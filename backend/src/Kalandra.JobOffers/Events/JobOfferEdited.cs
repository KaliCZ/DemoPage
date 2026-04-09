namespace Kalandra.JobOffers.Events;

public record JobOfferEdited(
    Guid EditedByUserId,
    string EditedByEmail,
    string CompanyName,
    string ContactName,
    string ContactEmail,
    string JobTitle,
    string Description,
    string? SalaryRange,
    string? Location,
    bool IsRemote,
    string? AdditionalNotes,
    DateTimeOffset Timestamp);
