namespace Kalandra.Api.Features.JobOffers.History;

public record JobOfferHistoryEntry(
    string EventType,
    string Description,
    string ActorEmail,
    DateTimeOffset Timestamp);

public record JobOfferHistoryResponse(List<JobOfferHistoryEntry> Entries);
