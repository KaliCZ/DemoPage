using Kalandra.JobOffers.Queries;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record HistoryEntryResponse(
    string EventType,
    string Description,
    Guid ActorUserId,
    string ActorEmail,
    DateTimeOffset Timestamp)
{
    public static HistoryEntryResponse Serialize(JobOfferHistoryEntry entry) => new(
        EventType: entry.EventType,
        Description: entry.Description,
        ActorUserId: entry.ActorUserId,
        ActorEmail: entry.ActorEmail,
        Timestamp: entry.Timestamp);
}

public record JobOfferHistoryResponse(
    List<HistoryEntryResponse> Entries,
    Dictionary<Guid, Uri> Avatars);
