using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Marten;

namespace Kalandra.Blog.Commands;

public record StoreBlogCommentCommand(Guid CommentsStreamId, BlogCommentPosted Comment);

public class StoreBlogCommentHandler(IDocumentSession session)
{
    public async Task<Result<BlogCommentPosted, PostBlogCommentError>> StoreAndSave(
        StoreBlogCommentCommand command, CancellationToken ct)
    {
        var streamId = command.CommentsStreamId;
        var comments = await session.Events.AggregateStreamAsync<BlogPostComments>(streamId, token: ct)
            ?? new BlogPostComments();

        // Idempotent under Temporal activity retries: a comment that already made
        // it onto the stream is reported as stored, never appended twice.
        if (comments.Comments.Any(c => c.CommentId == command.Comment.CommentId))
            return command.Comment;

        var result = comments.Post(command.Comment);
        if (result.Error is { } error)
            return error;

        session.Events.Append(streamId, command.Comment);
        await session.SaveChangesAsync(ct);
        return command.Comment;
    }
}
