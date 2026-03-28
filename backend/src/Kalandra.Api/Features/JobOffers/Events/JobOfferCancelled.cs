namespace Kalandra.Api.Features.JobOffers.Events;

public record JobOfferCancelled(
    string CancelledByUserId,
    string CancelledByEmail,
    string? Reason,
    DateTimeOffset Timestamp);
