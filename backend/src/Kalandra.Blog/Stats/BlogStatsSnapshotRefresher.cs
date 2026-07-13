using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Stats;

/// <summary>
/// Recomputes one post's public totals from the live views, reactions and comments and writes each
/// into its own snapshot column. This is the heavy aggregation lifted off the request path — the
/// background service drives it, and integration tests invoke it directly to make the snapshot
/// deterministic.
/// </summary>
public class BlogStatsSnapshotRefresher(IQuerySession session, BlogStatsSnapshotStore store)
{
    public async Task RefreshAsync(BlogPost post, CancellationToken ct)
    {
        await store.EnsureTableAsync(ct);

        var totalViews = await session.Query<BlogPostVisitorView>()
            .Where(view => view.Slug == post.Slug).SumAsync(view => view.ViewCount, ct);
        var uniqueVisitors = await session.CountDistinctViewersAsync(post.Slug, ct);
        var totalReactions = await session.Query<BlogReaction>()
            .Where(reaction => reaction.Slug == post.Slug).CountAsync(ct);
        // Omits tombstones, matching the "live discussion" count the endpoint has always reported.
        var comments = await session.Events.AggregateStreamAsync<BlogPostComments>(post.CommentsStreamId, token: ct);
        var totalComments = comments?.Comments.Count(comment => !comment.IsDeleted) ?? 0;

        await store.SetTotalViewsAsync(post.Slug, totalViews, ct);
        await store.SetUniqueVisitorsAsync(post.Slug, uniqueVisitors, ct);
        await store.SetTotalReactionsAsync(post.Slug, totalReactions, ct);
        await store.SetTotalCommentsAsync(post.Slug, totalComments, ct);
    }
}
