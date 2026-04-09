namespace Kalandra.JobOffers.Events;

public record JobOfferCancelled(
    Guid CancelledByUserId,
    string CancelledByEmail,
    string? Reason,
    DateTimeOffset Timestamp);
