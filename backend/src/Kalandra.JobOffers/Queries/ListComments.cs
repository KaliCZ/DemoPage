using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;

namespace Kalandra.JobOffers.Queries;

public record ListCommentsQuery(Guid JobOfferId, string UserId, bool IsAdmin);

public class ListCommentsHandler(IQuerySession session)
{
    public async Task<IReadOnlyList<JobOfferCommentAdded>?> HandleAsync(
        ListCommentsQuery query, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(query.JobOfferId, ct);
        if (offer == null)
            return null;

        if (!query.IsAdmin && offer.UserId != query.UserId)
            return null;

        var events = await session.Events.FetchStreamAsync(CommentStreamId.For(query.JobOfferId), token: ct);

        return events
            .Select(e => (JobOfferCommentAdded)e.Data)
            .ToList();
    }
}
