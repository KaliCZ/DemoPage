using Kalandra.Api.Features.JobOffers.Entities;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Cancel;

public class CancelJobOfferHandler(IDocumentSession session, TimeProvider timeProvider)
{
    public async Task<Try<Unit, CancelJobOfferError>> HandleAsync(
        Guid id,
        CancelJobOfferRequest request,
        string userId,
        string userEmail,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(id, ct);
        var offer = stream.Aggregate;
        if (offer == null)
            return Try.Error<Unit, CancelJobOfferError>(CancelJobOfferError.NotFound);

        var result = offer.Cancel(
            userId: userId,
            userEmail: userEmail,
            reason: request.Reason,
            timestamp: timeProvider.GetUtcNow());

        if (result.IsError)
            return result.Map<Unit>(_ => Unit.Value);

        stream.AppendOne(result.Success.Get((Unit _) => new InvalidOperationException()));
        await session.SaveChangesAsync(ct);
        return Try.Success<Unit, CancelJobOfferError>(Unit.Value);
    }
}
