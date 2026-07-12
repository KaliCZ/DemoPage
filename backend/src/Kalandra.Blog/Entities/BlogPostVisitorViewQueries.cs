using Marten;

namespace Kalandra.Blog.Entities;

public static class BlogPostVisitorViewQueries
{
    /// <summary>
    /// Distinct people who viewed the post: one per signed-in account (deduped across their
    /// devices) plus each anonymous visitor — the same identity as reactions (userId ?? visitorId).
    /// </summary>
    public static async Task<int> CountDistinctViewersAsync(this IQuerySession session, string slug, CancellationToken ct)
    {
        // Signed-in viewers are real accounts (few), so load and dedupe them in memory;
        // anonymous viewers are the bulk and stay a database-side count.
        var signedInViews = await session.Query<BlogPostVisitorView>()
            .Where(v => v.Slug == slug && v.UserId != null)
            .ToListAsync(ct);
        var anonymousViewers = await session.Query<BlogPostVisitorView>()
            .Where(v => v.Slug == slug && v.UserId == null)
            .CountAsync(ct);

        return signedInViews.Select(v => v.UserId).Distinct().Count() + anonymousViewers;
    }
}
