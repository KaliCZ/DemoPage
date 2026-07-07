using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;

namespace Kalandra.JobOffers.Commands;

public record StoreJobOfferCommand(Guid JobOfferId, JobOfferSubmitted Submitted);

public class StoreJobOfferHandler(IDocumentSession session)
{
    public async Task StoreAndSave(StoreJobOfferCommand command, CancellationToken ct)
    {
        // Idempotent under Temporal activity retries: an offer that already made
        // it into the store is never started twice.
        if (await session.LoadAsync<JobOffer>(command.JobOfferId, ct) != null)
            return;

        session.Events.StartStream<JobOffer>(command.JobOfferId, command.Submitted);
        await session.SaveChangesAsync(ct);
    }
}
