using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.UpdateStatus;

public record UpdateJobOfferStatusRequest(
    JobOfferStatus Status,
    string? AdminNotes);
