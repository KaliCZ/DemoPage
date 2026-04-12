namespace Kalandra.Api.Features.JobOffers.Contracts;

public record ListJobOffersResponse(
    IReadOnlyList<GetJobOfferDetailResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int PageCount,
    bool HasNextPage,
    bool HasPreviousPage);
