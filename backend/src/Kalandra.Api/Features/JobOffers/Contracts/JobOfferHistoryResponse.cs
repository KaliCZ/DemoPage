using Kalandra.JobOffers.Queries;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record FieldChangeResponse(string Field, string? OldValue, string? NewValue);

public record HistoryEntryResponse(
    string EventType,
    string Description,
    Guid ActorUserId,
    string ActorEmail,
    DateTimeOffset Timestamp,
    IEnumerable<FieldChangeResponse>? Changes)
{
    public static HistoryEntryResponse Serialize(JobOfferHistoryEntry entry) => new(
        EventType: entry.EventType,
        Description: entry.Description,
        ActorUserId: entry.ActorUserId,
        ActorEmail: entry.ActorEmail,
        Timestamp: entry.Timestamp,
        Changes: entry.Changes?.Select(c => new FieldChangeResponse(c.Field, c.OldValue, c.NewValue)));
}

public record JobOfferHistoryResponse(
    IEnumerable<HistoryEntryResponse> Entries);
