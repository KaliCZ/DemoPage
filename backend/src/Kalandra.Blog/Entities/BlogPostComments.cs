using Kalandra.Blog.Events;
using Kalandra.Infrastructure.Auth;

namespace Kalandra.Blog.Entities;

public enum PostBlogCommentError { ParentCommentNotFound, ParentCommentDeleted }
public enum DeleteBlogCommentError { CommentNotFound, NotAuthorized, AlreadyDeleted }

public class BlogPostComment
{
    public Guid CommentId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public Guid UserId { get; set; }
    public Email UserEmail { get; set; } = null!;
    public NonEmptyString AuthorDisplayName { get; set; } = null!;
    public Uri? AuthorAvatarUrl { get; set; }
    public NonEmptyString Content { get; set; } = null!;
    public DateTimeOffset PostedAt { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>
/// Live-aggregated state of one post's comment stream. Deletion is a tombstone:
/// the comment stays in the thread so replies keep their context.
/// </summary>
public class BlogPostComments
{
    public Guid Id { get; set; }
    public List<BlogPostComment> Comments { get; set; } = [];

    /// <summary>Takes the fully-built event because the caller (a Temporal workflow) owns the comment identity and timestamp.</summary>
    public Result<BlogCommentPosted, PostBlogCommentError> Post(BlogCommentPosted comment)
    {
        if (comment.ParentCommentId is { } parentId)
        {
            var parent = Comments.FirstOrDefault(c => c.CommentId == parentId);
            if (parent == null)
                return PostBlogCommentError.ParentCommentNotFound;
            if (parent.IsDeleted)
                return PostBlogCommentError.ParentCommentDeleted;
        }

        return comment;
    }

    public Result<BlogCommentDeleted, DeleteBlogCommentError> Delete(
        Guid commentId, CurrentUser user, DateTimeOffset timestamp)
    {
        var comment = Comments.FirstOrDefault(c => c.CommentId == commentId);
        if (comment == null)
            return DeleteBlogCommentError.CommentNotFound;

        if (comment.UserId != user.Id && !user.IsAdmin)
            return DeleteBlogCommentError.NotAuthorized;

        if (comment.IsDeleted)
            return DeleteBlogCommentError.AlreadyDeleted;

        return new BlogCommentDeleted(commentId, user.Id, timestamp);
    }

    public void Apply(BlogCommentPosted e)
    {
        Comments.Add(new BlogPostComment
        {
            CommentId = e.CommentId,
            ParentCommentId = e.ParentCommentId,
            UserId = e.UserId,
            UserEmail = e.UserEmail,
            AuthorDisplayName = e.AuthorDisplayName,
            AuthorAvatarUrl = e.AuthorAvatarUrl,
            Content = e.Content,
            PostedAt = e.Timestamp,
            IsDeleted = false,
        });
    }

    public void Apply(BlogCommentDeleted e)
    {
        var comment = Comments.FirstOrDefault(c => c.CommentId == e.CommentId);
        if (comment != null)
            comment.IsDeleted = true;
    }
}
