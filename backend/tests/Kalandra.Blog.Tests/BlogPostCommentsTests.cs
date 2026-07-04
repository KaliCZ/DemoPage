using System.Net.Mail;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Infrastructure.Auth;

namespace Kalandra.Blog.Tests;

public class BlogPostCommentsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid AuthorId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AdminId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly CurrentUser Author = new(AuthorId, new MailAddress("author@test.com"), "Author".ToNonEmpty(), []);
    private static readonly CurrentUser Other = new(OtherId, new MailAddress("other@test.com"), "Other".ToNonEmpty(), []);
    private static readonly CurrentUser Admin = new(AdminId, new MailAddress("admin@test.com"), "Admin".ToNonEmpty(), [UserRole.Admin]);

    private static BlogPostComments WithComment(Guid commentId, CurrentUser user, Guid? parentId = null)
    {
        var comments = new BlogPostComments();
        comments.Apply(new BlogCommentPosted(
            CommentId: commentId,
            ParentCommentId: parentId,
            UserId: user.Id,
            UserEmail: new Email(user.Email),
            AuthorDisplayName: user.FullName,
            AuthorAvatarUrl: null,
            Content: "A comment".ToNonEmpty(),
            Timestamp: Now));
        return comments;
    }

    // --- Post ---

    [Fact]
    public void Post_TopLevel_EmitsEventWithAuthorInfo()
    {
        var comments = new BlogPostComments();
        var commentId = Guid.NewGuid();

        var result = comments.Post(commentId, Author, "Hello!".ToNonEmpty(), parentCommentId: null, Now);

        Assert.True(result.IsSuccess);
        var posted = result.Success!;
        Assert.Equal(commentId, posted.CommentId);
        Assert.Null(posted.ParentCommentId);
        Assert.Equal(AuthorId, posted.UserId);
        Assert.Equal("Author", posted.AuthorDisplayName.Value);
        Assert.Equal("Hello!", posted.Content.Value);
        Assert.Equal(Now, posted.Timestamp);
    }

    [Fact]
    public void Post_ReplyToExistingComment_Succeeds()
    {
        var parentId = Guid.NewGuid();
        var comments = WithComment(parentId, Author);

        var result = comments.Post(Guid.NewGuid(), Other, "A reply".ToNonEmpty(), parentId, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(parentId, result.Success!.ParentCommentId);
    }

    [Fact]
    public void Post_ReplyToMissingParent_Fails()
    {
        var comments = new BlogPostComments();

        var result = comments.Post(Guid.NewGuid(), Other, "A reply".ToNonEmpty(), Guid.NewGuid(), Now);

        Assert.Equal(PostBlogCommentError.ParentCommentNotFound, result.Error);
    }

    [Fact]
    public void Post_ReplyToDeletedParent_Fails()
    {
        var parentId = Guid.NewGuid();
        var comments = WithComment(parentId, Author);
        comments.Apply(new BlogCommentDeleted(parentId, AuthorId, Now.AddMinutes(1)));

        var result = comments.Post(Guid.NewGuid(), Other, "A reply".ToNonEmpty(), parentId, Now.AddMinutes(2));

        Assert.Equal(PostBlogCommentError.ParentCommentDeleted, result.Error);
    }

    // --- Delete ---

    [Fact]
    public void Delete_OwnComment_EmitsTombstone()
    {
        var commentId = Guid.NewGuid();
        var comments = WithComment(commentId, Author);

        var result = comments.Delete(commentId, Author, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(commentId, result.Success!.CommentId);
        Assert.Equal(AuthorId, result.Success!.DeletedByUserId);
    }

    [Fact]
    public void Delete_SomeoneElsesComment_Fails()
    {
        var commentId = Guid.NewGuid();
        var comments = WithComment(commentId, Author);

        var result = comments.Delete(commentId, Other, Now.AddMinutes(1));

        Assert.Equal(DeleteBlogCommentError.NotAuthorized, result.Error);
    }

    [Fact]
    public void Delete_AsAdmin_ModeratesAnyComment()
    {
        var commentId = Guid.NewGuid();
        var comments = WithComment(commentId, Author);

        var result = comments.Delete(commentId, Admin, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(AdminId, result.Success!.DeletedByUserId);
    }

    [Fact]
    public void Delete_MissingComment_Fails()
    {
        var comments = new BlogPostComments();

        var result = comments.Delete(Guid.NewGuid(), Author, Now);

        Assert.Equal(DeleteBlogCommentError.CommentNotFound, result.Error);
    }

    [Fact]
    public void Delete_AlreadyDeletedComment_Fails()
    {
        var commentId = Guid.NewGuid();
        var comments = WithComment(commentId, Author);
        comments.Apply(new BlogCommentDeleted(commentId, AuthorId, Now.AddMinutes(1)));

        var result = comments.Delete(commentId, Author, Now.AddMinutes(2));

        Assert.Equal(DeleteBlogCommentError.AlreadyDeleted, result.Error);
    }

    // --- Apply ---

    [Fact]
    public void Apply_Tombstone_MarksCommentDeletedButKeepsThreadStructure()
    {
        var parentId = Guid.NewGuid();
        var replyId = Guid.NewGuid();
        var comments = WithComment(parentId, Author);
        comments.Apply(new BlogCommentPosted(
            CommentId: replyId,
            ParentCommentId: parentId,
            UserId: OtherId,
            UserEmail: new Email(Other.Email),
            AuthorDisplayName: Other.FullName,
            AuthorAvatarUrl: null,
            Content: "A reply".ToNonEmpty(),
            Timestamp: Now.AddMinutes(1)));

        comments.Apply(new BlogCommentDeleted(parentId, AuthorId, Now.AddMinutes(2)));

        Assert.Equal(2, comments.Comments.Count);
        Assert.True(comments.Comments.Single(c => c.CommentId == parentId).IsDeleted);
        Assert.False(comments.Comments.Single(c => c.CommentId == replyId).IsDeleted);
        Assert.Equal(parentId, comments.Comments.Single(c => c.CommentId == replyId).ParentCommentId);
    }
}
