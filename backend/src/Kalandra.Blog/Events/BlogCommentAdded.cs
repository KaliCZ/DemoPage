namespace Kalandra.Blog.Events;

public record BlogCommentAdded(
    Guid CommentId,
    string Slug,
    Guid UserId,
    string UserEmail,
    string UserName,
    string Content,
    DateTimeOffset Timestamp);
