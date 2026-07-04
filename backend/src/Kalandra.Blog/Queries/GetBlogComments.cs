using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogCommentsQuery(BlogPostSlug Slug);

public class GetBlogCommentsHandler(IQuerySession session)
{
    public async Task<BlogPostComments> HandleAsync(GetBlogCommentsQuery query, CancellationToken ct) =>
        await session.Events.AggregateStreamAsync<BlogPostComments>(BlogStreamId.ForComments(query.Slug), token: ct)
            ?? new BlogPostComments();
}
