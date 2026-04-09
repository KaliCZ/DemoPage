using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;

namespace Kalandra.JobOffers.Commands;

public record AddCommentCommand(
    Guid JobOfferId,
    CurrentUser User,
    NonEmptyString Content,
    DateTimeOffset Timestamp);

public class AddCommentHandler(IDocumentSession session)
{
    public async Task<Try<JobOfferCommentAdded, AddCommentError>> HandleAsync(
        AddCommentCommand command, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(command.JobOfferId, ct);
        if (offer == null)
            return Try.Error<JobOfferCommentAdded, AddCommentError>(AddCommentError.NotFound);

        if (!command.User.IsAdmin && offer.UserId != command.User.Id)
            return Try.Error<JobOfferCommentAdded, AddCommentError>(AddCommentError.NotAuthorized);

        var commentEvent = new JobOfferCommentAdded(
            CommentId: Guid.NewGuid(),
            UserId: command.User.Id,
            UserEmail: command.User.Email.Address,
            UserName: command.User.FullName,
            Content: command.Content.Value,
            Timestamp: command.Timestamp);

        session.Events.Append(CommentStreamId.For(command.JobOfferId), commentEvent);
        await session.SaveChangesAsync(ct);
        return Try.Success<JobOfferCommentAdded, AddCommentError>(commentEvent);
    }
}
