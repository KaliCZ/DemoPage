using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Cancel;

public class CancelJobOfferHandler(IDocumentSession session, TimeProvider timeProvider)
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

        var (success, error, cancelled) = offer.Cancel(
            userId,
            userEmail,
            request.Reason,
            timeProvider.GetUtcNow());

        if (!success || cancelled == null)
            return (false, error);

        stream.AppendOne(cancelled);
        await session.SaveChangesAsync(ct);
        return (true, null);
    }
}
