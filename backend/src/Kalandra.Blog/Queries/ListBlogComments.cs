using Kalandra.Blog.Events;
using Marten;

namespace Kalandra.Blog.Queries;

public record ListBlogCommentsQuery(NonEmptyString Slug);

public class ListBlogCommentsHandler(IQuerySession session)
{
    public async Task<IReadOnlyList<BlogCommentAdded>> HandleAsync(
        ListBlogCommentsQuery query, CancellationToken ct)
    {
        var streamId = BlogStreamId.ForComments(query.Slug);
        var events = await session.Events.FetchStreamAsync(streamId, token: ct);

        return events
            .Select(e => (BlogCommentAdded)e.Data)
            .ToList();
    }
}
