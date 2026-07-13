using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogPostStatsQuery(IReadOnlyList<BlogPost> Posts, Guid? ViewerId);

/// <summary>ViewerViews is null for anonymous callers — they have no signed-in reading history to report.</summary>
public record BlogPostStats(string Slug, int TotalViews, int UniqueVisitors, int TotalReactions, int TotalComments, int? ViewerViews);

/// <summary>
/// Aggregates the blog-index stats for the whole requested batch. Views, reactions and the viewer's own
/// reads are set-based GROUP BY queries — one each, not a per-post loop. Comment counts stay a live stream
/// fold per post (cheap for now; optimizing them is tracked in #221).
/// </summary>
public class GetBlogPostStatsHandler(IQuerySession session)
{
    public async Task<IReadOnlyList<BlogPostStats>> List(GetBlogPostStatsQuery query, CancellationToken ct)
    {
        if (query.Posts.Count == 0)
            return [];

        var slugs = query.Posts.Select(post => post.Slug).ToArray();

        var totalViews = await SumViewCountBySlugAsync(view => slugs.Contains(view.Slug), ct);
        var uniqueVisitors = await CountDistinctViewersBySlugAsync(slugs, ct);
        var totalReactions = await CountReactionsBySlugAsync(slugs, ct);
        var viewerViews = query.ViewerId is { } viewerId
            ? await SumViewCountBySlugAsync(view => slugs.Contains(view.Slug) && view.UserId == viewerId, ct)
            : null;

        var stats = new List<BlogPostStats>(query.Posts.Count);
        foreach (var post in query.Posts)
        {
            // Omits tombstones, matching the "live discussion" count the endpoint has always reported.
            var comments = await session.Events.AggregateStreamAsync<BlogPostComments>(post.CommentsStreamId, token: ct);
            var totalComments = comments?.Comments.Count(comment => !comment.IsDeleted) ?? 0;

            stats.Add(new BlogPostStats(
                Slug: post.Slug,
                TotalViews: totalViews.GetValueOrDefault(post.Slug),
                UniqueVisitors: uniqueVisitors.GetValueOrDefault(post.Slug),
                TotalReactions: totalReactions.GetValueOrDefault(post.Slug),
                TotalComments: totalComments,
                ViewerViews: viewerViews is null ? null : viewerViews.GetValueOrDefault(post.Slug)));
        }

        return stats;
    }

    private async Task<Dictionary<string, int>> SumViewCountBySlugAsync(
        System.Linq.Expressions.Expression<Func<BlogPostVisitorView, bool>> filter, CancellationToken ct)
    {
        var rows = await session.Query<BlogPostVisitorView>()
            .Where(filter)
            .GroupBy(view => view.Slug)
            .Select(group => new { Slug = group.Key, Total = group.Sum(view => view.ViewCount) })
            .ToListAsync(ct);
        return rows.ToDictionary(row => row.Slug, row => row.Total, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, int>> CountReactionsBySlugAsync(string[] slugs, CancellationToken ct)
    {
        var rows = await session.Query<BlogReaction>()
            .Where(reaction => slugs.Contains(reaction.Slug))
            .GroupBy(reaction => reaction.Slug)
            .Select(group => new { Slug = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(row => row.Slug, row => row.Count, StringComparer.Ordinal);
    }

    /// <summary>
    /// Distinct people per post = each anonymous visitor (a DB-side count) plus every distinct signed-in
    /// account (deduped across devices). Marten can't translate COUNT(DISTINCT COALESCE(user_id, visitor_id)),
    /// so the signed-in rows — real accounts, few of them — are deduped in memory, mirroring CountDistinctViewersAsync.
    /// </summary>
    private async Task<Dictionary<string, int>> CountDistinctViewersBySlugAsync(string[] slugs, CancellationToken ct)
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
