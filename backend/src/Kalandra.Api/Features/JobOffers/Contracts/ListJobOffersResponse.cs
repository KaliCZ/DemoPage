using Kalandra.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record JobOfferSummary(
    Guid Id,
    string CompanyName,
    string JobTitle,
    string ContactEmail,
    JobOfferStatus Status,
    bool IsRemote,
    string? Location,
    DateTimeOffset CreatedAt)
{
    public static JobOfferSummary Serialize(JobOffer offer) => new(
        Id: offer.Id,
        CompanyName: offer.CompanyName,
        JobTitle: offer.JobTitle,
        ContactEmail: offer.ContactEmail,
        Status: offer.Status,
        IsRemote: offer.IsRemote,
        Location: offer.Location,
        CreatedAt: offer.CreatedAt);
}

public record ListJobOffersResponse(IReadOnlyList<JobOfferSummary> Items, int TotalCount);
