using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogReactionsQuery(BlogPostSlug Slug);

public class GetBlogReactionsHandler(IQuerySession session)
{
    /// <summary>
    /// A slug with no stream yet returns empty state, not null — the backend
    /// cannot know which posts exist, so "no reactions" is the honest answer.
    /// </summary>
    public async Task<BlogPostReactions> HandleAsync(GetBlogReactionsQuery query, CancellationToken ct) =>
        await session.Events.AggregateStreamAsync<BlogPostReactions>(BlogStreamId.ForReactions(query.Slug), token: ct)
            ?? new BlogPostReactions();
}
