namespace Kalandra.Api.Features.JobOffers.Contracts;

public record HistoryEntryResponse(
    string EventType,
    string Description,
    string ActorUserId,
    string ActorEmail,
    DateTimeOffset Timestamp,
    string? AvatarUrl = null);

public record JobOfferHistoryResponse(List<HistoryEntryResponse> Entries);
