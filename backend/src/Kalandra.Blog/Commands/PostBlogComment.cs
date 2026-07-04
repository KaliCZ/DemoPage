using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Infrastructure.Auth;
using Marten;

namespace Kalandra.Blog.Commands;

public record PostBlogCommentCommand(
    BlogPostSlug Slug,
    CurrentUser User,
    NonEmptyString Content,
    Guid? ParentCommentId,
    DateTimeOffset Timestamp);

public class PostBlogCommentHandler(IDocumentSession session)
{
    public async Task<Result<BlogCommentPosted, PostBlogCommentError>> HandleAsync(
        PostBlogCommentCommand command, CancellationToken ct)
    {
        var streamId = BlogStreamId.ForComments(command.Slug);
        var comments = await session.Events.AggregateStreamAsync<BlogPostComments>(streamId, token: ct)
            ?? new BlogPostComments();

        var result = comments.Post(
            commentId: Guid.NewGuid(),
            user: command.User,
            content: command.Content,
            parentCommentId: command.ParentCommentId,
            timestamp: command.Timestamp);

        if (result.Error is { } error)
            return error;

        var posted = result.Success!;
        session.Events.Append(streamId, posted);
        await session.SaveChangesAsync(ct);
        return posted;
    }
}
