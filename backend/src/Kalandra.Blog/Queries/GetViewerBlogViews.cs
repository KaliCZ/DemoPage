using System.Collections.ObjectModel;
using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record ViewerBlogViewsQuery(IReadOnlyCollection<string> Slugs, Guid ViewerId);

/// <summary>
/// The signed-in reader's own view count per post, summed across their devices — the read-status
/// signal for a post list, without the heavier per-post totals GetBlogPostStats also computes.
/// </summary>
public class GetViewerBlogViewsHandler(IQuerySession session)
{
    public async Task<IReadOnlyDictionary<string, int>> List(ViewerBlogViewsQuery query, CancellationToken ct)
    {
        if (query.Slugs.Count == 0)
            return ReadOnlyDictionary<string, int>.Empty;

        var slugs = query.Slugs as string[] ?? [.. query.Slugs];
        var rows = await session.Query<BlogPostVisitorView>()
            .Where(view => view.UserId == query.ViewerId && slugs.Contains(view.Slug))
            .ToListAsync(ct);

        return rows
            .GroupBy(view => view.Slug, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(view => view.ViewCount), StringComparer.Ordinal);
    }
}
