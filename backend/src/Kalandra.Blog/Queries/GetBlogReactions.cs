using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Marten;

namespace Kalandra.Blog.Queries;

/// <summary>
/// Reaction state on a post: total per emoji, plus the set of emoji the
/// requesting user has reacted with. <paramref name="UserId"/> is null for
/// anonymous viewers — the response shape is the same, just with no
/// per-user reactions to highlight.
/// </summary>
public record GetBlogReactionsQuery(NonEmptyString Slug, Guid? UserId);

public record BlogReactionsView(
    IReadOnlyDictionary<BlogReactionEmoji, int> Counts,
    IReadOnlyList<BlogReactionEmoji> UserReactions);

public class GetBlogReactionsHandler(IQuerySession session)
{
    public async Task<BlogReactionsView> HandleAsync(GetBlogReactionsQuery query, CancellationToken ct)
    {
        var streamId = BlogStreamId.ForReactions(query.Slug);
        var events = await session.Events.FetchStreamAsync(streamId, token: ct);

        // Replay: a (user, emoji) pair is "active" iff the most recent event for
        // that pair is an Added. Tracking presence per pair lets us derive both
        // the totals and the requesting user's own set in one pass.
        var active = new HashSet<(Guid UserId, BlogReactionEmoji Emoji)>();
        foreach (var e in events)
        {
            switch (e.Data)
            {
                case BlogReactionAdded a:
                    active.Add((a.UserId, a.Emoji));
                    break;
                case BlogReactionRemoved r:
                    active.Remove((r.UserId, r.Emoji));
                    break;
            }
        }

        var counts = active
            .GroupBy(p => p.Emoji)
            .ToDictionary(g => g.Key, g => g.Count());

        var userReactions = query.UserId is { } uid
            ? active.Where(p => p.UserId == uid).Select(p => p.Emoji).ToList()
            : new List<BlogReactionEmoji>();

        return new BlogReactionsView(counts, userReactions);
    }
}
