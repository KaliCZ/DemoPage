using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;
using StrongTypes;

namespace Kalandra.JobOffers.Commands;

public record StoreJobOfferCommentCommand(Guid JobOfferId, JobOfferCommentAdded Comment, bool CommenterIsAdmin);

/// <summary>The stored comment plus the offer fields that notification planning and email bodies need.</summary>
public record StoredJobOfferComment(
    JobOfferCommentAdded Comment,
    Guid OfferAuthorUserId,
    Email OfferAuthorEmail,
    NonEmptyString CompanyName,
    NonEmptyString JobTitle);

public class StoreJobOfferCommentHandler(IDocumentSession session)
{
    public async Task<Result<StoredJobOfferComment, AddCommentError>> StoreAndSave(
        StoreJobOfferCommentCommand command, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(command.JobOfferId, ct);
        if (offer == null)
            return AddCommentError.NotFound;

        if (!command.CommenterIsAdmin && offer.UserId != command.Comment.UserId)
            return AddCommentError.NotAuthorized;

        var stored = new StoredJobOfferComment(
            Comment: command.Comment,
            OfferAuthorUserId: offer.UserId,
            OfferAuthorEmail: offer.UserEmail,
            CompanyName: offer.CompanyName,
            JobTitle: offer.JobTitle);

        // Idempotent under Temporal activity retries: a comment that already made
        // it onto the stream is reported as stored, never appended twice.
        var streamId = CommentStreamId.For(command.JobOfferId);
        var events = await session.Events.FetchStreamAsync(streamId, token: ct);
        if (events.Any(e => e.Data is JobOfferCommentAdded existing && existing.CommentId == command.Comment.CommentId))
            return stored;

        session.Events.Append(streamId, command.Comment);
        await session.SaveChangesAsync(ct);
        return stored;
    }
}
