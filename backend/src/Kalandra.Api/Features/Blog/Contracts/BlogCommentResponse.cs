using Kalandra.Blog.Events;

namespace Kalandra.Api.Features.Blog.Contracts;

public record BlogCommentResponse(
    Guid Id,
    Guid UserId,
    string UserEmail,
    string UserName,
    string Content,
    DateTimeOffset CreatedAt)
{
    public static BlogCommentResponse Serialize(BlogCommentAdded comment) => new(
        Id: comment.CommentId,
        UserId: comment.UserId,
        UserEmail: comment.UserEmail,
        UserName: comment.UserName,
        Content: comment.Content,
        CreatedAt: comment.Timestamp);
}

public record ListBlogCommentsResponse(IEnumerable<BlogCommentResponse> Comments);
