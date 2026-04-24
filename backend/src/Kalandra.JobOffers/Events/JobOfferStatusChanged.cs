using Kalandra.JobOffers.Entities;

namespace Kalandra.JobOffers.Events;

public record JobOfferStatusChanged(
    Guid ChangedByUserId,
    NonEmptyString ChangedByEmail,
    JobOfferStatus OldStatus,
    JobOfferStatus NewStatus,
    string? Notes,
    DateTimeOffset Timestamp);
