using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogPostStatsQuery(IReadOnlyList<BlogPost> Posts, Guid? ViewerId);

/// <summary>ViewerViews is null for anonymous callers — they have no signed-in reading history to report.</summary>
public record BlogPostStats(string Slug, int TotalViews, int UniqueVisitors, int TotalReactions, int? ViewerViews);

public class GetBlogPostStatsHandler(IQuerySession session)
{
    public async Task<IReadOnlyList<BlogPostStats>> List(GetBlogPostStatsQuery query, CancellationToken ct)
    {
        var stats = new List<BlogPostStats>(query.Posts.Count);
        foreach (var post in query.Posts)
        {
            // The slug column is duplicated + indexed, so these are aggregate queries, not row loads.
            var totalViews = await session.Query<BlogPostVisitorView>()
                .Where(v => v.Slug == post.Slug).SumAsync(v => v.ViewCount, ct);
            // One document per (post, visitor), so the row count is the number of distinct visitors.
            var uniqueVisitors = await session.Query<BlogPostVisitorView>()
                .Where(v => v.Slug == post.Slug).CountAsync(ct);

            // Reaction streams are live-aggregated (see ConfigureBlog); posts are few, so one replay each is fine.
            var reactions = await session.Events.AggregateStreamAsync<BlogPostReactions>(post.ReactionsStreamId, token: ct);

            int? viewerViews = query.ViewerId is { } viewerId
                ? await session.Query<BlogPostVisitorView>()
                    .Where(v => v.Slug == post.Slug && v.UserId == viewerId).SumAsync(v => v.ViewCount, ct)
                : null;

            stats.Add(new BlogPostStats(
                Slug: post.Slug,
                TotalViews: totalViews,
                UniqueVisitors: uniqueVisitors,
                TotalReactions: reactions?.TotalCount() ?? 0,
                ViewerViews: viewerViews));
        }

        return stats;
    }
}
