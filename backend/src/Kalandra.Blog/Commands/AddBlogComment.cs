using Kalandra.Blog.Events;
using Kalandra.Infrastructure.Auth;
using Marten;

namespace Kalandra.Blog.Commands;

public record AddBlogCommentCommand(
    NonEmptyString Slug,
    CurrentUser User,
    NonEmptyString Content,
    DateTimeOffset Timestamp);

public class AddBlogCommentHandler(IDocumentSession session)
{
    public async Task<BlogCommentAdded> HandleAsync(AddBlogCommentCommand command, CancellationToken ct)
    {
        var commentEvent = new BlogCommentAdded(
            CommentId: Guid.NewGuid(),
            Slug: command.Slug.Value,
            UserId: command.User.Id,
            UserEmail: command.User.Email.Address,
            UserName: command.User.FullName,
            Content: command.Content.Value,
            Timestamp: command.Timestamp);

        session.Events.Append(BlogStreamId.ForComments(command.Slug), commentEvent);
        await session.SaveChangesAsync(ct);
        return commentEvent;
    }
}
