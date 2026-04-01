using Kalandra.Api.Features.JobOffers.Entities;
using Marten;

namespace Kalandra.Api.Features.JobOffers.UpdateStatus;

public class UpdateJobOfferStatusHandler(IDocumentSession session, TimeProvider timeProvider)
{
    public async Task<Try<Unit, UpdateJobOfferStatusError>> HandleAsync(
        Guid id,
        UpdateJobOfferStatusRequest request,
        string adminUserId,
        string adminEmail,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(id, ct);
        var offer = stream.Aggregate;
        if (offer == null)
            return Try.Error<Unit, UpdateJobOfferStatusError>(UpdateJobOfferStatusError.NotFound);

        var result = offer.ChangeStatus(
            newStatus: request.Status,
            changedByUserId: adminUserId,
            changedByEmail: adminEmail,
            notes: request.AdminNotes,
            timestamp: timeProvider.GetUtcNow());

        if (result.IsError)
            return result.Map<Unit>(_ => Unit.Value);

        stream.AppendOne(result.Success.Get((Unit _) => new InvalidOperationException()));
        await session.SaveChangesAsync(ct);
        return Try.Success<Unit, UpdateJobOfferStatusError>(Unit.Value);
    }
}
