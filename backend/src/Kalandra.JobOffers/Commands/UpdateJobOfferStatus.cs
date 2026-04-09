using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Marten;

namespace Kalandra.JobOffers.Commands;

public record UpdateJobOfferStatusCommand(
    Guid Id,
    JobOfferStatus NewStatus,
    CurrentUser User,
    string? Notes,
    DateTimeOffset Timestamp);

public class UpdateJobOfferStatusHandler(IDocumentSession session)
{
    public async Task<Try<JobOffer, UpdateJobOfferStatusError>> HandleAsync(
        UpdateJobOfferStatusCommand command, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(command.Id, ct);
        if (stream.Aggregate is not { } offer)
            return Try.Error<JobOffer, UpdateJobOfferStatusError>(UpdateJobOfferStatusError.NotFound);

        var result = offer.ChangeStatus(
            newStatus: command.NewStatus,
            user: command.User,
            notes: command.Notes,
            timestamp: command.Timestamp);

        if (result.IsError)
            return Try.Error<JobOffer, UpdateJobOfferStatusError>(result.Error.Get());

        var evt = result.Success.Get();
        stream.AppendOne(evt);
        offer.Apply(evt);
        await session.SaveChangesAsync(ct);
        return Try.Success<JobOffer, UpdateJobOfferStatusError>(offer);
    }
}
