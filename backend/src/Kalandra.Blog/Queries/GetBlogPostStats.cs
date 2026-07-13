using System.Linq.Expressions;
using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogPostStatsQuery(IReadOnlyList<BlogPost> Posts, Guid? ViewerId);

/// <summary>ViewerViews is null for anonymous callers — they have no signed-in reading history to report.</summary>
public record BlogPostStats(string Slug, int TotalViews, int UniqueVisitors, int TotalReactions, int TotalComments, int? ViewerViews);

/// <summary>
/// Aggregates the blog-index stats for the whole requested batch. Views, reactions and comments each run
/// on their own Marten session, so the three groups overlap on separate connections instead of summing.
/// Within a group the queries share one session (a Marten session runs one query at a time). Comment
/// counts stay a live per-post stream fold — cheap for now, optimizing them is tracked in #221.
/// </summary>
public class GetBlogPostStatsHandler(IDocumentStore store)
{
    public async Task<IReadOnlyList<BlogPostStats>> List(GetBlogPostStatsQuery query, CancellationToken ct)
    {
        if (query.Posts.Count == 0)
            return [];

        var slugs = query.Posts.Select(post => post.Slug).ToArray();

        var viewsTask = ReadViewStatsAsync(slugs, query.ViewerId, ct);
        var reactionsTask = ReadReactionCountsAsync(slugs, ct);
        var commentsTask = ReadCommentCountsAsync(query.Posts, ct);
        await Task.WhenAll(viewsTask, reactionsTask, commentsTask);

        var (totalViews, uniqueVisitors, viewerViews) = await viewsTask;
        var totalReactions = await reactionsTask;
        var totalComments = await commentsTask;

        return
        [
            .. query.Posts.Select(post => new BlogPostStats(
                Slug: post.Slug,
                TotalViews: totalViews.GetValueOrDefault(post.Slug),
                UniqueVisitors: uniqueVisitors.GetValueOrDefault(post.Slug),
                TotalReactions: totalReactions.GetValueOrDefault(post.Slug),
                TotalComments: totalComments.GetValueOrDefault(post.CommentsStreamId),
                ViewerViews: viewerViews is null ? null : viewerViews.GetValueOrDefault(post.Slug))),
        ];
    }

    private async Task<(Dictionary<string, int> Total, Dictionary<string, int> Unique, Dictionary<string, int>? Viewer)> ReadViewStatsAsync(
        string[] slugs, Guid? viewerId, CancellationToken ct)
    {
        await using var session = store.QuerySession();
        var total = await SumViewCountBySlugAsync(session, view => slugs.Contains(view.Slug), ct);
        var unique = await CountDistinctViewersBySlugAsync(session, slugs, ct);
        var viewer = viewerId is { } id
            ? await SumViewCountBySlugAsync(session, view => slugs.Contains(view.Slug) && view.UserId == id, ct)
            : null;
        return (total, unique, viewer);
    }

    private async Task<Dictionary<string, int>> ReadReactionCountsAsync(string[] slugs, CancellationToken ct)
    {
        await using var session = store.QuerySession();
        var rows = await session.Query<BlogReaction>()
            .Where(reaction => slugs.Contains(reaction.Slug))
            .GroupBy(reaction => reaction.Slug)
            .Select(group => new { Slug = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(row => row.Slug, row => row.Count, StringComparer.Ordinal);
    }

    private async Task<Dictionary<Guid, int>> ReadCommentCountsAsync(IReadOnlyList<BlogPost> posts, CancellationToken ct)
    {
        await using var session = store.QuerySession();
        var result = new Dictionary<Guid, int>();
        foreach (var post in posts)
        {
            // Omits tombstones, matching the "live discussion" count the endpoint has always reported.
            var comments = await session.Events.AggregateStreamAsync<BlogPostComments>(post.CommentsStreamId, token: ct);
            result[post.CommentsStreamId] = comments?.Comments.Count(comment => !comment.IsDeleted) ?? 0;
        }
        return result;
    }

    private static async Task<Dictionary<string, int>> SumViewCountBySlugAsync(
        IQuerySession session, Expression<Func<BlogPostVisitorView, bool>> filter, CancellationToken ct)
    {
        var rows = await session.Query<BlogPostVisitorView>()
            .Where(filter)
            .GroupBy(view => view.Slug)
            .Select(group => new { Slug = group.Key, Total = group.Sum(view => view.ViewCount) })
            .ToListAsync(ct);
        return rows.ToDictionary(row => row.Slug, row => row.Total, StringComparer.Ordinal);
    }

    /// <summary>
    /// Distinct people per post = each anonymous visitor (a DB-side count) plus every distinct signed-in
    /// account (deduped across devices). Marten can't translate COUNT(DISTINCT COALESCE(user_id, visitor_id)),
    /// so the signed-in rows — real accounts, few of them — are deduped in memory, mirroring CountDistinctViewersAsync.
    /// </summary>
    private static async Task<Dictionary<string, int>> CountDistinctViewersBySlugAsync(
        IQuerySession session, string[] slugs, CancellationToken ct)
    {
        var anonymous = await session.Query<BlogPostVisitorView>()
            .Where(view => slugs.Contains(view.Slug) && view.UserId == null)
            .GroupBy(view => view.Slug)
            .Select(group => new { Slug = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        var signedIn = await session.Query<BlogPostVisitorView>()
            .Where(view => slugs.Contains(view.Slug) && view.UserId != null)
            .Select(view => new { view.Slug, view.UserId })
            .ToListAsync(ct);

        var result = anonymous.ToDictionary(row => row.Slug, row => row.Count, StringComparer.Ordinal);
        foreach (var group in signedIn.GroupBy(view => view.Slug, StringComparer.Ordinal))
            result[group.Key] = result.GetValueOrDefault(group.Key) + group.Select(view => view.UserId).Distinct().Count();
        return result;
    }
}
