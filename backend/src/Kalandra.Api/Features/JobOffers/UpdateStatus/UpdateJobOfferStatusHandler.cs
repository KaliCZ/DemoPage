using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.UpdateStatus;

public class UpdateJobOfferStatusHandler
{
    private readonly IDocumentSession _session;

    public UpdateJobOfferStatusHandler(IDocumentSession session)
    {
        _session = session;
    }

    public async Task<bool> HandleAsync(
        Guid id,
        UpdateJobOfferStatusRequest request,
        string adminUserId,
        string adminEmail,
        CancellationToken ct)
    {
        var offer = await _session.Events.AggregateStreamAsync<JobOffer>(id, token: ct);
        if (offer == null)
            return false;

        var statusChanged = new JobOfferStatusChanged(
            ChangedByUserId: adminUserId,
            ChangedByEmail: adminEmail,
            OldStatus: offer.Status,
            NewStatus: request.Status,
            Notes: request.AdminNotes,
            Timestamp: DateTimeOffset.UtcNow);

        _session.Events.Append(id, statusChanged);
        await _session.SaveChangesAsync(ct);
        return true;
    }
}
