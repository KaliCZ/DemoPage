using Kalandra.JobOffers.Queries;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record JobOfferHistoryResponse(List<JobOfferHistoryEntry> Entries);
