using Kalandra.Api.Infrastructure;
using Kalandra.JobOffers.Contracts;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record ListJobOffersResponse(
    IReadOnlyList<GetJobOfferDetailResponse> Items,
    PaginationMetadata Pagination);
