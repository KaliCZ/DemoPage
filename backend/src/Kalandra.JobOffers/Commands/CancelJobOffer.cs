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
    public async Task<Result<JobOffer, CancelJobOfferError>> HandleAsync(
        CancelJobOfferCommand command, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(command.Id, ct);
        if (stream.Aggregate is not { } offer)
            return CancelJobOfferError.NotFound;

        var result = offer.Cancel(
            user: command.User,
            reason: command.Reason,
            timestamp: command.Timestamp);

        if (result.Error is { } error)
            return error;

        var evt = result.Success!;
        stream.AppendOne(evt);
        offer.Apply(evt);
        await session.SaveChangesAsync(ct);
        return offer;
    }
}
