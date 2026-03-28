using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.List;

public record JobOfferSummary(
    Guid Id,
    string CompanyName,
    string JobTitle,
    string ContactEmail,
    JobOfferStatus Status,
    bool IsRemote,
    string? Location,
    DateTimeOffset CreatedAt);

public record ListJobOffersResponse(List<JobOfferSummary> Items, int TotalCount);
