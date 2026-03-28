namespace Kalandra.Api.Features.JobOffers.Create;

public record CreateJobOfferRequest(
    string CompanyName,
    string ContactName,
    string ContactEmail,
    string JobTitle,
    string Description,
    string? SalaryRange,
    string? Location,
    bool IsRemote,
    string? AdditionalNotes);
