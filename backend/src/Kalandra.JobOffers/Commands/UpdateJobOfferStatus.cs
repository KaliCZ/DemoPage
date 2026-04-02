using Kalandra.JobOffers.Entities;
using Marten;

namespace Kalandra.JobOffers.Commands;

public record UpdateJobOfferStatusCommand(
    Guid Id,
    JobOfferStatus NewStatus,
    NonEmptyString ChangedByUserId,
    NonEmptyString ChangedByEmail,
    string? Notes,
    DateTimeOffset Timestamp);

/// <summary>
/// Validates and appends a status change event. Does not save — the caller commits the session.
/// </summary>
public class UpdateJobOfferStatusHandler(IDocumentSession session)
{
    public async Task<Try<Unit, UpdateJobOfferStatusError>> HandleAsync(
        UpdateJobOfferStatusCommand command, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(command.Id, ct);
        if (stream.Aggregate is not { } offer)
            return Try.Error<Unit, UpdateJobOfferStatusError>(UpdateJobOfferStatusError.NotFound);

        var result = offer.ChangeStatus(
            newStatus: command.NewStatus,
            changedByUserId: command.ChangedByUserId.Value,
            changedByEmail: command.ChangedByEmail.Value,
            notes: command.Notes,
            timestamp: command.Timestamp);

        return result.Match(
            evt =>
            {
                stream.AppendOne(evt);
                return Try.Success<Unit, UpdateJobOfferStatusError>(Unit.Value);
            },
            err => Try.Error<Unit, UpdateJobOfferStatusError>(err));
    }
}
