namespace Kalandra.Blog.Events;

public record BlogCommentPosted(
    Guid CommentId,
    Guid? ParentCommentId,
    Guid UserId,
    Email UserEmail,
    NonEmptyString AuthorDisplayName,
    Uri? AuthorAvatarUrl,
    NonEmptyString Content,
    DateTimeOffset Timestamp);
