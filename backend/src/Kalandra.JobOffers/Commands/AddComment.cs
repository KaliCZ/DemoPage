using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;
using StrongTypes;

namespace Kalandra.JobOffers.Commands;

public record AddCommentCommand(
    Guid JobOfferId,
    CurrentUser User,
    NonEmptyString Content,
    DateTimeOffset Timestamp);

public class AddCommentHandler(IDocumentSession session)
{
    public async Task<Result<JobOfferCommentAdded, AddCommentError>> HandleAsync(
        AddCommentCommand command, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(command.JobOfferId, ct);
        if (offer == null)
            return AddCommentError.NotFound;

        if (!command.User.IsAdmin && offer.UserId != command.User.Id)
            return AddCommentError.NotAuthorized;

        var commentEvent = new JobOfferCommentAdded(
            CommentId: Guid.NewGuid(),
            UserId: command.User.Id,
            UserEmail: new Email(command.User.Email),
            UserName: command.User.FullName,
            Content: command.Content,
            Timestamp: command.Timestamp);

        session.Events.Append(CommentStreamId.For(command.JobOfferId), commentEvent);
        await session.SaveChangesAsync(ct);
        return commentEvent;
    }
}
