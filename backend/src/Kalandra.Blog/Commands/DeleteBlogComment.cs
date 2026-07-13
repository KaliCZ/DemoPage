using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Infrastructure.Auth;
using Marten;

namespace Kalandra.Blog.Commands;

public record DeleteBlogCommentCommand(
    Guid CommentsStreamId,
    Guid CommentId,
    CurrentUser User,
    DateTimeOffset Timestamp);

public class DeleteBlogCommentHandler(IDocumentSession session, BlogCommentCountCache commentCountCache)
{
    public async Task<Result<BlogCommentDeleted, DeleteBlogCommentError>> DeleteAndSave(
        DeleteBlogCommentCommand command, CancellationToken ct)
    {
        var streamId = command.CommentsStreamId;
        var comments = await session.Events.AggregateStreamAsync<BlogPostComments>(streamId, token: ct)
            ?? new BlogPostComments();

        var result = comments.Delete(command.CommentId, command.User, command.Timestamp);
        if (result.Error is { } error)
            return error;

        var deleted = result.Success!;
        session.Events.Append(streamId, deleted);
        await session.SaveChangesAsync(ct);
        // A tombstone drops the live count, so the cached value is stale until wiped.
        commentCountCache.Invalidate(streamId);
        return deleted;
    }
}
