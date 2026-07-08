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
        // Returning the existing id is silent retry protection — we assume the same user
        // sending the same id has just submitted the form twice.
        if (await session.LoadAsync<JobOffer>(command.JobOfferId, ct) is { } existing)
            return existing.UserId == command.Submitted.UserId
                ? command.JobOfferId
                : CreateJobOfferError.IdAlreadyUsed;

        session.Events.StartStream<JobOffer>(command.JobOfferId, command.Submitted);
        await session.SaveChangesAsync(ct);
        return command.JobOfferId;
    }
}
