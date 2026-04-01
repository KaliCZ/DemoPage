using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Comments;

public class ListCommentsHandler(IQuerySession session)
{
    public async Task<ListCommentsResponse?> HandleAsync(
        Guid jobOfferId,
        string? requesterUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(jobOfferId, ct);
        if (offer == null)
            return null;

        if (!isAdmin && offer.UserId != requesterUserId)
            return null;

        var events = await session.Events.FetchStreamAsync(jobOfferId, token: ct);

        var comments = events
            .Where(e => e.Data is JobOfferCommentAdded)
            .Select(e => (JobOfferCommentAdded)e.Data)
            .Select(c => new CommentResponse(
                c.CommentId,
                c.UserId,
                c.UserEmail,
                c.UserName,
                c.Content,
                c.Timestamp))
            .ToList();

        return new ListCommentsResponse(comments);
    }
}
