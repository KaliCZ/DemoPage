using Kalandra.Blog.Entities;
using Kalandra.Blog.Stats;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogPostStatsQuery(IReadOnlyList<BlogPost> Posts, Guid? ViewerId);

/// <summary>ViewerViews is null for anonymous callers — they have no signed-in reading history to report.</summary>
public record BlogPostStats(string Slug, int TotalViews, int UniqueVisitors, int TotalReactions, int TotalComments, int? ViewerViews);

public class GetBlogPostStatsHandler(IQuerySession session, BlogStatsSnapshotStore snapshots)
{
    public async Task<IReadOnlyList<BlogPostStats>> List(GetBlogPostStatsQuery query, CancellationToken ct)
    {
        var slugs = query.Posts.Select(post => post.Slug).ToArray();

        // The public totals are a by-id read of the async snapshot; the heavy aggregation runs in the background.
        var snapshotsBySlug = await snapshots.LoadAsync(slugs, ct);

        // The viewer's own read count is per-person, so it can't live in the shared snapshot — one
        // indexed query resolves it across every requested post, and only when the caller is signed in.
        IReadOnlyDictionary<string, int>? viewerViewsBySlug = query.ViewerId is { } viewerId
            ? (await session.Query<BlogPostVisitorView>()
                    .Where(view => view.UserId == viewerId && slugs.Contains(view.Slug))
                    .ToListAsync(ct))
                .GroupBy(view => view.Slug, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(view => view.ViewCount), StringComparer.Ordinal)
            : null;

        return
        [
            .. query.Posts.Select(post =>
            {
                var snapshot = snapshotsBySlug.GetValueOrDefault(post.Slug);
                return new BlogPostStats(
                    Slug: post.Slug,
                    TotalViews: snapshot?.TotalViews ?? 0,
                    UniqueVisitors: snapshot?.UniqueVisitors ?? 0,
                    TotalReactions: snapshot?.TotalReactions ?? 0,
                    TotalComments: snapshot?.TotalComments ?? 0,
                    // Signed-in but unseen posts read 0; anonymous callers get null.
                    ViewerViews: viewerViewsBySlug is null ? null : viewerViewsBySlug.GetValueOrDefault(post.Slug));
            }),
        ];
    }
}
