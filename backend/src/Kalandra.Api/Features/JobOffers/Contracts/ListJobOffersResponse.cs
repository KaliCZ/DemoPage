using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record JobOfferSummary(
    Guid Id,
    string CompanyName,
    string JobTitle,
    string ContactEmail,
    JobOfferStatus Status,
    bool IsRemote,
    string? Location,
    DateTimeOffset CreatedAt);

public record ListJobOffersResponse(IReadOnlyList<JobOfferSummary> Items, int TotalCount);
