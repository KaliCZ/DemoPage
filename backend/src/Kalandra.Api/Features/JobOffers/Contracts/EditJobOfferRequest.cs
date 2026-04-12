namespace Kalandra.Api.Features.JobOffers.Contracts;

public record EditJobOfferRequest(
    string? CompanyName,
    string? ContactName,
    string? ContactEmail,
    string? JobTitle,
    string? Description,
    string? SalaryRange,
    string? Location,
    bool? IsRemote,
    string? AdditionalNotes);
