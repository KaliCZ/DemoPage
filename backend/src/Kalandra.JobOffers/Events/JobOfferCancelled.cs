using StrongTypes;

namespace Kalandra.JobOffers.Events;

public record JobOfferCancelled(
    Guid CancelledByUserId,
    Email CancelledByEmail,
    string? Reason,
    DateTimeOffset Timestamp);
