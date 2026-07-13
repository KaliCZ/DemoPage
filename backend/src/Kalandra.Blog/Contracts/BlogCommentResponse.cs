using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;

namespace Kalandra.Blog.Contracts;

public record BlogCommentResponse(
    Guid Id,
    Guid? ParentCommentId,
    Guid? UserId,
    NonEmptyString? AuthorDisplayName,
    Uri? AuthorAvatarUrl,
    NonEmptyString? Content,
    DateTimeOffset PostedAt,
    bool IsDeleted)
{
    /// <summary>Deleted comments keep their place in the thread but drop author and content.</summary>
    public static BlogCommentResponse Serialize(BlogPostComment comment) => comment.IsDeleted
        ? new BlogCommentResponse(
            Id: comment.CommentId,
            ParentCommentId: comment.ParentCommentId,
            UserId: null,
            AuthorDisplayName: null,
            AuthorAvatarUrl: null,
            Content: null,
            PostedAt: comment.PostedAt,
            IsDeleted: true)
        : new BlogCommentResponse(
            Id: comment.CommentId,
            ParentCommentId: comment.ParentCommentId,
            UserId: comment.UserId,
            AuthorDisplayName: comment.AuthorDisplayName,
            AuthorAvatarUrl: comment.AuthorAvatarUrl,
            Content: comment.Content,
            PostedAt: comment.PostedAt,
            IsDeleted: false);

    public static BlogCommentResponse Serialize(BlogCommentPosted posted) => new(
        Id: posted.CommentId,
        ParentCommentId: posted.ParentCommentId,
        UserId: posted.UserId,
        AuthorDisplayName: posted.AuthorDisplayName,
        AuthorAvatarUrl: posted.AuthorAvatarUrl,
        Content: posted.Content,
        PostedAt: posted.Timestamp,
        IsDeleted: false);
}

public record ListBlogCommentsResponse(IReadOnlyList<BlogCommentResponse> Comments)
{
    public static ListBlogCommentsResponse Serialize(BlogPostComments comments) => new(
        [.. comments.Comments.OrderBy(c => c.PostedAt).Select(BlogCommentResponse.Serialize)]);
}
