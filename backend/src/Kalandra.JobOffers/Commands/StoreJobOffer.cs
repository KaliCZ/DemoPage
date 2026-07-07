using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;
using StrongTypes;

namespace Kalandra.JobOffers.Commands;

public record StoreJobOfferCommand(Guid JobOfferId, JobOfferSubmitted Submitted);

public class StoreJobOfferHandler(IDocumentSession session)
{
    public async Task<Result<Guid, CreateJobOfferError>> StoreAndSave(StoreJobOfferCommand command, CancellationToken ct)
    {
        // Idempotent under Temporal activity retries and client resends: the submitter's
        // own offer already in the store is a success, never started twice.
        if (await session.LoadAsync<JobOffer>(command.JobOfferId, ct) is { } existing)
            return existing.UserId == command.Submitted.UserId
                ? command.JobOfferId
                : CreateJobOfferError.IdAlreadyUsed;

        session.Events.StartStream<JobOffer>(command.JobOfferId, command.Submitted);
        await session.SaveChangesAsync(ct);
        return command.JobOfferId;
    }
}
