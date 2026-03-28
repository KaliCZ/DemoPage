using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.Events;

public record JobOfferStatusChanged(
    string ChangedByUserId,
    string ChangedByEmail,
    JobOfferStatus OldStatus,
    JobOfferStatus NewStatus,
    string? Notes,
    DateTimeOffset Timestamp);
