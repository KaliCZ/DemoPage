using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Marten;

namespace Kalandra.Blog.Commands;

public record PostBlogCommentCommand(BlogPost Post, BlogCommentPosted Comment);

public class PostBlogCommentHandler(IDocumentSession session)
{
    /// <summary>
    /// Stores the comment; the notification emails are delivered separately by the
    /// blog-comment subscription reacting to the appended event.
    /// </summary>
    public async Task<Result<BlogCommentPosted, PostBlogCommentError>> PostAndSave(
        PostBlogCommentCommand command, CancellationToken ct)
    {
        var streamId = command.Post.CommentsStreamId;
        var comments = await session.Events.AggregateStreamAsync<BlogPostComments>(streamId, token: ct)
            ?? new BlogPostComments();

        // A client resend of the same comment id is reported as stored, never appended twice.
        if (comments.Comments.Any(c => c.CommentId == command.Comment.CommentId))
            return command.Comment;

        if (comments.Post(command.Comment).Error is { } error)
            return error;

        session.Events.Append(streamId, command.Comment);
        await session.SaveChangesAsync(ct);
        return command.Comment;
    }
}
