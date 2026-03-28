namespace Kalandra.Api.Features.JobOffers.Events;

public record JobOfferSubmitted(
    string UserId,
    string UserEmail,
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
