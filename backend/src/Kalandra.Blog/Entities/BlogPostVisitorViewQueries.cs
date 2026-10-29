using Marten;

namespace Kalandra.Blog.Entities;

public static class BlogPostVisitorViewQueries
{
    /// <summary>A viewer is identified as userId ?? visitorId, the same identity reactions use.</summary>
    public static async Task<int> CountDistinctViewersAsync(this IQuerySession session, string slug, CancellationToken ct)
    {
        // Signed-in viewers are few, so they're deduped in memory; the anonymous bulk stays a database-side count.
        var signedInViews = await session.Query<BlogPostVisitorView>()
            .Where(v => v.Slug == slug && v.UserId != null)
            .ToListAsync(ct);
        var anonymousViewers = await session.Query<BlogPostVisitorView>()
            .Where(v => v.Slug == slug && v.UserId == null)
            .CountAsync(ct);

        return signedInViews.Select(v => v.UserId).Distinct().Count() + anonymousViewers;
    }
}
