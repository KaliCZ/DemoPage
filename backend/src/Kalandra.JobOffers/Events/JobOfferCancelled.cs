namespace Kalandra.JobOffers.Events;

public record JobOfferCancelled(
    Guid CancelledByUserId,
    NonEmptyString CancelledByEmail,
    string? Reason,
    DateTimeOffset Timestamp);
