using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.GetDetail;

public record GetJobOfferDetailResponse(
    Guid Id,
    string CompanyName,
    string ContactName,
    string ContactEmail,
    string JobTitle,
    string Description,
    string? SalaryRange,
    string? Location,
    bool IsRemote,
    string? AdditionalNotes,
    JobOfferStatus Status,
    string? AdminNotes,
    string UserEmail,
    DateTime CreatedAt,
    DateTime UpdatedAt);
