namespace Kalandra.Blog.Events;

public record BlogCommentDeleted(
    Guid CommentId,
    Guid DeletedByUserId,
    DateTimeOffset Timestamp);
