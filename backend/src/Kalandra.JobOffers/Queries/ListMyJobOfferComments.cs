using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;
using Marten.Linq;

namespace Kalandra.JobOffers.Queries;

public record ListMyJobOfferCommentsQuery(CurrentUser User);

public record MyJobOfferComment(JobOffer Offer, JobOfferCommentAdded Comment, IReadOnlyList<JobOfferCommentAdded> Replies);

public class ListMyJobOfferCommentsHandler(IQuerySession session)
{
    public async Task<IReadOnlyList<MyJobOfferComment>> List(ListMyJobOfferCommentsQuery query, CancellationToken ct)
    {
        var q = session.Query<JobOffer>();

        // Non-admins can only comment on their own offers, so scanning those covers all their comments.
        if (!query.User.IsAdmin)
        {
            var userId = query.User.Id;
            q = (IMartenQueryable<JobOffer>)q.Where(j => j.UserId == userId);
        }

        var offers = await q.ToListAsync(ct);

        var results = new List<MyJobOfferComment>();
        foreach (var offer in offers)
        {
            var events = await session.Events.FetchStreamAsync(CommentStreamId.For(offer.Id), token: ct);
            if (events.Count == 0)
                continue;

            var comments = events.Select(e => (JobOfferCommentAdded)e.Data).ToList();
            results.AddRange(Collect(offer, comments, query.User.Id));
        }

        return results.OrderByDescending(r => r.Comment.Timestamp).ToList();
    }

    /// <summary>
    /// Job-offer threads are flat, so a "reply" is inferred by position: each other
    /// author's comment attaches to my latest comment that precedes it.
    /// </summary>
    public static IEnumerable<MyJobOfferComment> Collect(
        JobOffer offer, IReadOnlyList<JobOfferCommentAdded> comments, Guid userId)
    {
        var mine = new List<(JobOfferCommentAdded Comment, List<JobOfferCommentAdded> Replies)>();
        foreach (var comment in comments.OrderBy(c => c.Timestamp))
        {
            if (comment.UserId == userId)
                mine.Add((comment, []));
            else if (mine.Count > 0)
                mine[^1].Replies.Add(comment);
        }

        return mine.Select(m => new MyJobOfferComment(offer, m.Comment, m.Replies));
    }
}
