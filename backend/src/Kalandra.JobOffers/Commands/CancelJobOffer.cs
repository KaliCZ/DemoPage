using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Marten;

namespace Kalandra.JobOffers.Commands;

public record CancelJobOfferCommand(
    Guid Id,
    CurrentUser User,
    string? Reason,
    DateTimeOffset Timestamp);

public class CancelJobOfferHandler(IDocumentSession session)
{
    public async Task<Try<JobOffer, CancelJobOfferError>> HandleAsync(
        CancelJobOfferCommand command, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(command.Id, ct);
        if (stream.Aggregate is not { } offer)
            return Try.Error<JobOffer, CancelJobOfferError>(CancelJobOfferError.NotFound);

        var result = offer.Cancel(
            userId: command.User.Id,
            userEmail: command.User.Email.Address,
            reason: command.Reason,
            timestamp: command.Timestamp);

        if (result.IsError)
            return Try.Error<JobOffer, CancelJobOfferError>(result.Error.Get());

        var evt = result.Success.Get();
        stream.AppendOne(evt);
        offer.Apply(evt);
        await session.SaveChangesAsync(ct);
        return Try.Success<JobOffer, CancelJobOfferError>(offer);
    }
}
