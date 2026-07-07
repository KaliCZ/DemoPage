using Kalandra.Blog.Entities;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogCommentsQuery(Guid CommentsStreamId);

public class GetBlogCommentsHandler(IQuerySession session)
{
    public async Task<BlogPostComments> Get(GetBlogCommentsQuery query, CancellationToken ct) =>
        await session.Events.AggregateStreamAsync<BlogPostComments>(query.CommentsStreamId, token: ct)
            ?? new BlogPostComments();
}
