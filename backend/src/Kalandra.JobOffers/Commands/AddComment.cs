using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;
using StrongTypes;

namespace Kalandra.JobOffers.Commands;

public record AddCommentCommand(
    Guid JobOfferId,
    Guid CommentId,
    CurrentUser User,
    NonEmptyString Content,
    DateTimeOffset Timestamp);

public class AddCommentHandler(IDocumentSession session)
{
    /// <summary>
    /// Stores the comment; the notification emails are delivered separately by the
    /// job-offer subscription reacting to the appended event. Re-checks authorization here
    /// (not just at the HTTP boundary) because the write is the domain's last line of defence.
    /// </summary>
    public async Task<Result<JobOfferCommentAdded, AddCommentError>> AddAndSave(
        AddCommentCommand command, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(command.JobOfferId, ct);
        if (offer == null)
            return AddCommentError.NotFound;

        if (!command.User.IsAdmin && offer.UserId != command.User.Id)
            return AddCommentError.NotAuthorized;

        var comment = new JobOfferCommentAdded(
            JobOfferId: command.JobOfferId,
            CommentId: command.CommentId,
            UserId: command.User.Id,
            UserEmail: command.User.Email,
            UserName: command.User.FullName,
            Content: command.Content,
            Timestamp: command.Timestamp);

        // A client resend of the same comment id is reported as stored, never appended twice.
        var streamId = CommentStreamId.For(command.JobOfferId);
        var events = await session.Events.FetchStreamAsync(streamId, token: ct);
        if (events.Any(e => e.Data is JobOfferCommentAdded existing && existing.CommentId == command.CommentId))
            return comment;

        session.Events.Append(streamId, comment);
        await session.SaveChangesAsync(ct);
        return comment;
    }
}
