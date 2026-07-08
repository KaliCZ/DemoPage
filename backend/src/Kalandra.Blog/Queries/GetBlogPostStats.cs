using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogPostStatsQuery(IReadOnlyList<BlogPost> Posts, Guid? ViewerId);

/// <summary>ViewerReads is null for anonymous callers — they have no read history to report.</summary>
public record BlogPostStats(string Slug, int TotalReads, int TotalReactions, int? ViewerReads);

public class GetBlogPostStatsHandler(IQuerySession session)
{
    public async Task<IReadOnlyList<BlogPostStats>> List(GetBlogPostStatsQuery query, CancellationToken ct)
    {
        var readsByStreamId = (await session.LoadManyAsync<BlogPostReads>(ct, query.Posts.Select(post => post.ReadsStreamId).ToArray()))
            .ToDictionary(reads => reads.Id);

        var stats = new List<BlogPostStats>(query.Posts.Count);
        foreach (var post in query.Posts)
        {
            // Reaction streams are live-aggregated (see ConfigureBlog); posts are few, so one replay each is fine.
            var reactions = await session.Events.AggregateStreamAsync<BlogPostReactions>(post.ReactionsStreamId, token: ct);
            var reads = readsByStreamId.GetValueOrDefault(post.ReadsStreamId);
            stats.Add(new BlogPostStats(
                Slug: post.Slug,
                TotalReads: reads?.TotalReads ?? 0,
                TotalReactions: reactions?.TotalCount() ?? 0,
                ViewerReads: query.ViewerId is { } viewerId ? reads?.CountFor(viewerId) ?? 0 : null));
        }

        return stats;
    }
}
