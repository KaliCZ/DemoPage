using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Marten;

namespace Kalandra.JobOffers.Commands;

public record CancelJobOfferCommand(
    Guid Id,
    CurrentUser User,
    string? Reason,
    DateTimeOffset Timestamp);

/// <summary>
/// Validates and appends a cancel event. Does not save — the caller commits the session.
/// </summary>
public class CancelJobOfferHandler(IDocumentSession session)
{
    public async Task<Try<Unit, CancelJobOfferError>> HandleAsync(
        CancelJobOfferCommand command, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(command.Id, ct);
        if (stream.Aggregate is not { } offer)
            return Try.Error<Unit, CancelJobOfferError>(CancelJobOfferError.NotFound);

        var result = offer.Cancel(
            userId: command.User.Id,
            userEmail: command.User.Email.Address,
            reason: command.Reason,
            timestamp: command.Timestamp);

        return result.Match(
            evt =>
            {
                stream.AppendOne(evt);
                return Try.Success<Unit, CancelJobOfferError>(Unit.Value);
            },
            err => Try.Error<Unit, CancelJobOfferError>(err));
    }
}
