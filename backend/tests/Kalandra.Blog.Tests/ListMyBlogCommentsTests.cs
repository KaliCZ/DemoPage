using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Blog.Queries;

namespace Kalandra.Blog.Tests;

public class ListMyBlogCommentsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid MyId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = new("22222222-2222-2222-2222-222222222222");

    private static readonly BlogPost Post = new(
        Slug: "test-post",
        CommentsStreamId: Guid.NewGuid());

    private static BlogCommentPosted NewComment(
        Guid userId, Guid? parentId = null, string content = "A comment", int minutesAfterNow = 0) => new(
        CommentId: Guid.NewGuid(),
        ParentCommentId: parentId,
        UserId: userId,
        UserEmail: Email.Create($"{userId}@test.com"),
        AuthorDisplayName: "Someone".ToNonEmpty(),
        AuthorAvatarUrl: null,
        Content: content.ToNonEmpty(),
        Timestamp: Now.AddMinutes(minutesAfterNow));

    [Fact]
    public void Collect_ReturnsMyCommentsWithDirectReplies()
    {
        var comments = new BlogPostComments();
        var mine = NewComment(MyId, content: "Mine");
        var reply = NewComment(OtherId, parentId: mine.CommentId, content: "Reply to you", minutesAfterNow: 1);
        var unrelated = NewComment(OtherId, content: "Unrelated top-level", minutesAfterNow: 2);
        comments.Apply(mine);
        comments.Apply(reply);
        comments.Apply(unrelated);

        var result = ListMyBlogCommentsHandler.Collect(Post, comments, MyId).ToList();

        var entry = Assert.Single(result);
        Assert.Equal(Post, entry.Post);
        Assert.Equal(mine.CommentId, entry.Comment.CommentId);
        Assert.Equal(reply.CommentId, Assert.Single(entry.Replies).CommentId);
    }

    [Fact]
    public void Collect_ExcludesDeletedCommentsAndDeletedReplies()
    {
        var comments = new BlogPostComments();
        var deletedMine = NewComment(MyId, content: "Deleted mine");
        var keptMine = NewComment(MyId, content: "Kept mine", minutesAfterNow: 1);
        var deletedReply = NewComment(OtherId, parentId: keptMine.CommentId, minutesAfterNow: 2);
        comments.Apply(deletedMine);
        comments.Apply(keptMine);
        comments.Apply(deletedReply);
        comments.Apply(new BlogCommentDeleted(deletedMine.CommentId, MyId, Now.AddMinutes(3)));
        comments.Apply(new BlogCommentDeleted(deletedReply.CommentId, OtherId, Now.AddMinutes(3)));

        var result = ListMyBlogCommentsHandler.Collect(Post, comments, MyId).ToList();

        var entry = Assert.Single(result);
        Assert.Equal(keptMine.CommentId, entry.Comment.CommentId);
        Assert.Empty(entry.Replies);
    }

    [Fact]
    public void Collect_ReturnsNothingForUserWithoutComments()
    {
        var comments = new BlogPostComments();
        comments.Apply(NewComment(OtherId));

        Assert.Empty(ListMyBlogCommentsHandler.Collect(Post, comments, MyId));
    }

    [Fact]
    public void Collect_OrdersRepliesByPostedAt()
    {
        var comments = new BlogPostComments();
        var mine = NewComment(MyId);
        var late = NewComment(OtherId, parentId: mine.CommentId, content: "Late", minutesAfterNow: 10);
        var early = NewComment(OtherId, parentId: mine.CommentId, content: "Early", minutesAfterNow: 1);
        comments.Apply(mine);
        comments.Apply(late);
        comments.Apply(early);

        var entry = Assert.Single(ListMyBlogCommentsHandler.Collect(Post, comments, MyId));

        Assert.Equal([early.CommentId, late.CommentId], entry.Replies.Select(r => r.CommentId));
    }
}
