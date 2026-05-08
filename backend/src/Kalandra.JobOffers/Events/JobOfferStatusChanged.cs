using Kalandra.JobOffers.Entities;
using StrongTypes;

namespace Kalandra.JobOffers.Events;

public record JobOfferStatusChanged(
    Guid ChangedByUserId,
    Email ChangedByEmail,
    JobOfferStatus OldStatus,
    JobOfferStatus NewStatus,
    string? Notes,
    DateTimeOffset Timestamp);
