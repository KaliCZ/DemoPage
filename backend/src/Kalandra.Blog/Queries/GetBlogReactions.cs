using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogReactionsQuery(Guid ReactionsStreamId);

public class GetBlogReactionsHandler(IQuerySession session)
{
    /// <summary>A post with no reactions yet has no stream, so aggregation returns null — treat that as empty state.</summary>
    public async Task<BlogPostReactions> Get(GetBlogReactionsQuery query, CancellationToken ct) =>
        await session.Events.AggregateStreamAsync<BlogPostReactions>(query.ReactionsStreamId, token: ct)
            ?? new BlogPostReactions();
}
