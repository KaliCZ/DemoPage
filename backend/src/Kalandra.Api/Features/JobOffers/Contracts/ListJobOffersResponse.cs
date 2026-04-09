namespace Kalandra.Api.Features.JobOffers.Contracts;

public record ListJobOffersResponse(IReadOnlyList<GetJobOfferDetailResponse> Items, int TotalCount);
