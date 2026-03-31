using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.UpdateStatus;

public class UpdateJobOfferStatusHandler(IDocumentSession session)
{
    public async Task<(bool Success, string? Error)> HandleAsync(
        Guid id,
        UpdateJobOfferStatusRequest request,
        string adminUserId,
        string adminEmail,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(id, ct);
        var offer = stream.Aggregate;
        if (offer == null)
            return (false, "Not found");

        var (success, error, statusChanged) = offer.ChangeStatus(
            request.Status,
            adminUserId,
            adminEmail,
            request.AdminNotes,
            DateTimeOffset.UtcNow);

        if (!success || statusChanged == null)
            return (false, error);

        stream.AppendOne(statusChanged);
        await session.SaveChangesAsync(ct);
        return (true, null);
    }
}
