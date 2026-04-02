using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;

namespace Kalandra.JobOffers.Commands;

public record AddCommentCommand(
    Guid JobOfferId,
    NonEmptyString UserId,
    NonEmptyString UserEmail,
    NonEmptyString UserName,
    NonEmptyString Content,
    bool IsAdmin,
    DateTimeOffset Timestamp);

/// <summary>
/// Validates authorization and appends a comment event. Does not save — the caller commits the session.
/// </summary>
public class AddCommentHandler(IDocumentSession session)
{
    public async Task<Try<JobOfferCommentAdded, AddCommentError>> HandleAsync(
        AddCommentCommand command, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(command.JobOfferId, ct);
        if (offer == null)
            return Try.Error<JobOfferCommentAdded, AddCommentError>(AddCommentError.NotFound);

        if (!command.IsAdmin && offer.UserId != command.UserId.Value)
            return Try.Error<JobOfferCommentAdded, AddCommentError>(AddCommentError.NotAuthorized);

        var commentEvent = new JobOfferCommentAdded(
            CommentId: Guid.NewGuid(),
            UserId: command.UserId.Value,
            UserEmail: command.UserEmail.Value,
            UserName: command.UserName.Value,
            Content: command.Content.Value,
            Timestamp: command.Timestamp);

        session.Events.Append(CommentStreamId.For(command.JobOfferId), commentEvent);
        return Try.Success<JobOfferCommentAdded, AddCommentError>(commentEvent);
    }
}
