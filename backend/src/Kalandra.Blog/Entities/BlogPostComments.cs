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

    public Result<BlogCommentPosted, PostBlogCommentError> Post(
        Guid commentId,
        CurrentUser user,
        NonEmptyString content,
        Guid? parentCommentId,
        DateTimeOffset timestamp)
    {
        if (parentCommentId is { } parentId)
        {
            var parent = Comments.FirstOrDefault(c => c.CommentId == parentId);
            if (parent == null)
                return PostBlogCommentError.ParentCommentNotFound;
            if (parent.IsDeleted)
                return PostBlogCommentError.ParentCommentDeleted;
        }

        return new BlogCommentPosted(
            CommentId: commentId,
            ParentCommentId: parentCommentId,
            UserId: user.Id,
            UserEmail: user.Email,
            AuthorDisplayName: user.FullName,
            AuthorAvatarUrl: user.AvatarUrl,
            Content: content,
            Timestamp: timestamp);
    }

    public Result<BlogCommentDeleted, DeleteBlogCommentError> Delete(
        Guid commentId, CurrentUser user, DateTimeOffset timestamp)
    {
        var comment = Comments.FirstOrDefault(c => c.CommentId == commentId);
        if (comment == null)
            return DeleteBlogCommentError.CommentNotFound;

        // Admins can moderate any thread; everyone else only deletes their own.
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
