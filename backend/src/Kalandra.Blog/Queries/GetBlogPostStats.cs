using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogPostStatsQuery(IReadOnlyList<BlogPost> Posts, Guid? ViewerId);

/// <summary>ViewerViews is null for anonymous callers — they have no signed-in reading history to report.</summary>
public record BlogPostStats(string Slug, int TotalViews, int UniqueVisitors, int TotalReactions, int TotalComments, int? ViewerViews);

public class GetBlogPostStatsHandler(IQuerySession session)
{
    public async Task<IReadOnlyList<BlogPostStats>> List(GetBlogPostStatsQuery query, CancellationToken ct)
    {
        var stats = new List<BlogPostStats>(query.Posts.Count);
        foreach (var post in query.Posts)
        {
            // The slug column is duplicated + indexed, so this is an aggregate query, not a row load.
            var totalViews = await session.Query<BlogPostVisitorView>()
                .Where(v => v.Slug == post.Slug).SumAsync(v => v.ViewCount, ct);
            var uniqueVisitors = await session.CountDistinctViewersAsync(post.Slug, ct);

            // Each reaction is one row keyed by its reactor, so the row count is already deduped per person.
            var totalReactions = await session.Query<BlogReaction>()
                .Where(reaction => reaction.Slug == post.Slug).CountAsync(ct);

            // Unlike the on-page thread, this omits tombstones — it's a measure of live discussion.
            var comments = await session.Events.AggregateStreamAsync<BlogPostComments>(post.CommentsStreamId, token: ct);
            var totalComments = comments?.Comments.Count(c => !c.IsDeleted) ?? 0;

            int? viewerViews = query.ViewerId is { } viewerId
                ? await session.Query<BlogPostVisitorView>()
                    .Where(v => v.Slug == post.Slug && v.UserId == viewerId).SumAsync(v => v.ViewCount, ct)
                : null;

            stats.Add(new BlogPostStats(
                Slug: post.Slug,
                TotalViews: totalViews,
                UniqueVisitors: uniqueVisitors,
                TotalReactions: totalReactions,
                TotalComments: totalComments,
                ViewerViews: viewerViews));
        }

        return stats;
    }
}
