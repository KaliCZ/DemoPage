using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Cancel;

public class CancelJobOfferHandler
{
    private readonly IDocumentSession _session;

    public CancelJobOfferHandler(IDocumentSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Cancel a job offer. Only the owner can cancel, and only if status is Submitted or InReview.
    /// </summary>
    public async Task<(bool Success, string? Error)> HandleAsync(
        Guid id,
        CancelJobOfferRequest request,
        string userId,
        string userEmail,
        CancellationToken ct)
    {
        var offer = await _session.Events.AggregateStreamAsync<JobOffer>(id, token: ct);
        if (offer == null)
            return (false, "Not found");

        if (offer.UserId != userId)
            return (false, "Not authorized");

        if (offer.Status is not (JobOfferStatus.Submitted or JobOfferStatus.InReview))
            return (false, "Cannot cancel an offer that has already been accepted, declined, or cancelled");

        var cancelled = new JobOfferCancelled(
            CancelledByUserId: userId,
            CancelledByEmail: userEmail,
            Reason: request.Reason,
            Timestamp: DateTimeOffset.UtcNow);

        _session.Events.Append(id, cancelled);
        await _session.SaveChangesAsync(ct);
        return (true, null);
    }
}
