using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Cancel;

public class CancelJobOfferHandler(IDocumentSession session)
{
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
        var stream = await session.Events.FetchForWriting<JobOffer>(id, ct);
        var offer = stream.Aggregate;
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

        stream.AppendOne(cancelled);
        await session.SaveChangesAsync(ct);
        return (true, null);
    }
}
