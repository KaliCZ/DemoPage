using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.UpdateStatus;

public class UpdateJobOfferStatusHandler(IDocumentSession session)
{
    public async Task<bool> HandleAsync(
        Guid id,
        UpdateJobOfferStatusRequest request,
        string adminUserId,
        string adminEmail,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(id, ct);
        var offer = stream.Aggregate;
        if (offer == null)
            return false;

        var statusChanged = new JobOfferStatusChanged(
            ChangedByUserId: adminUserId,
            ChangedByEmail: adminEmail,
            OldStatus: offer.Status,
            NewStatus: request.Status,
            Notes: request.AdminNotes,
            Timestamp: DateTimeOffset.UtcNow);

        stream.AppendOne(statusChanged);
        await session.SaveChangesAsync(ct);
        return true;
    }
}
