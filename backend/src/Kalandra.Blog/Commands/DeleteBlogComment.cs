using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Infrastructure.Auth;
using Marten;

namespace Kalandra.Blog.Commands;

public record DeleteBlogCommentCommand(
    BlogPostSlug Slug,
    Guid CommentId,
    CurrentUser User,
    DateTimeOffset Timestamp);

public class DeleteBlogCommentHandler(IDocumentSession session)
{
    public async Task<Result<BlogCommentDeleted, DeleteBlogCommentError>> HandleAsync(
        DeleteBlogCommentCommand command, CancellationToken ct)
    {
        var streamId = BlogStreamId.ForComments(command.Slug);
        var comments = await session.Events.AggregateStreamAsync<BlogPostComments>(streamId, token: ct)
            ?? new BlogPostComments();

        var result = comments.Delete(command.CommentId, command.User, command.Timestamp);
        if (result.Error is { } error)
            return error;

        var deleted = result.Success!;
        session.Events.Append(streamId, deleted);
        await session.SaveChangesAsync(ct);
        return deleted;
    }
}
