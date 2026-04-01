namespace Kalandra.Api.Features.JobOffers.Contracts;

public record JobOfferHistoryEntry(
    string EventType,
    string Description,
    string ActorEmail,
    DateTimeOffset Timestamp);

public record JobOfferHistoryResponse(List<JobOfferHistoryEntry> Entries);
