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
        var offer = await session.Events.AggregateStreamAsync<JobOffer>(id, token: ct);
        if (offer == null)
            return false;

        var statusChanged = new JobOfferStatusChanged(
            ChangedByUserId: adminUserId,
            ChangedByEmail: adminEmail,
            OldStatus: offer.Status,
            NewStatus: request.Status,
            Notes: request.AdminNotes,
            Timestamp: DateTimeOffset.UtcNow);

        session.Events.Append(id, statusChanged);
        await session.SaveChangesAsync(ct);
        return true;
    }
}
