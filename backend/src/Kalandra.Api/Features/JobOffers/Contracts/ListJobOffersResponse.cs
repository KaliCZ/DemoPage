using Kalandra.Api.Infrastructure;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record ListJobOffersResponse(
    IReadOnlyList<GetJobOfferDetailResponse> Items,
    PaginationMetadata Pagination);
