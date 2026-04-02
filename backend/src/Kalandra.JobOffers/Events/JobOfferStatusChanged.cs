using Kalandra.JobOffers.Entities;

namespace Kalandra.JobOffers.Events;

public record JobOfferStatusChanged(
    string ChangedByUserId,
    string ChangedByEmail,
    JobOfferStatus OldStatus,
    JobOfferStatus NewStatus,
    string? Notes,
    DateTimeOffset Timestamp);
