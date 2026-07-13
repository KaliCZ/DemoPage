using System.Net.Mail;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Blog.Notifications;

namespace Kalandra.Blog.Tests;

public class BlogCommentNotificationsTests
{
    private static readonly MailAddress AuthorEmail = new("author@kalandra.local");
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    private static BlogCommentPosted Comment(string email, Guid? userId = null, Guid? parentId = null) => new(
        CommentId: Guid.NewGuid(),
        ParentCommentId: parentId,
        UserId: userId ?? Guid.NewGuid(),
        UserEmail: Email.Create(email),
        AuthorDisplayName: "Commenter".ToNonEmpty(),
        AuthorAvatarUrl: null,
        Content: "Hello".ToNonEmpty(),
        Timestamp: Now);

    private static BlogPostComment Parent(string email, Guid? userId = null, bool isDeleted = false) => new()
    {
        CommentId = Guid.NewGuid(),
        UserId = userId ?? Guid.NewGuid(),
        UserEmail = Email.Create(email),
        AuthorDisplayName = "Parent".ToNonEmpty(),
        Content = "Original".ToNonEmpty(),
        PostedAt = Now.AddMinutes(-5),
        IsDeleted = isDeleted,
    };

    [Fact]
    public void TopLevelComment_NotifiesOnlyTheAuthor()
    {
        var notifications = BlogCommentNotifications.Plan(Comment("visitor@test.com"), parent: null, AuthorEmail);

        var notification = Assert.Single(notifications);
        Assert.Equal(BlogCommentNotificationKind.NewCommentForAuthor, notification.Kind);
        Assert.Equal("author@kalandra.local", notification.Recipient.Address);
    }

    [Fact]
    public void Reply_NotifiesAuthorAndParentCommentAuthor()
    {
        var parent = Parent("parent@test.com");

        var notifications = BlogCommentNotifications.Plan(Comment("visitor@test.com"), parent, AuthorEmail);

        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, n => n.Kind == BlogCommentNotificationKind.NewCommentForAuthor);
        Assert.Contains(notifications, n =>
            n.Kind == BlogCommentNotificationKind.ReplyToYourComment && n.Recipient.Address == "parent@test.com");
    }

    [Fact]
    public void ReplyToYourOwnComment_DoesNotNotifyYourself()
    {
        var userId = Guid.NewGuid();
        var parent = Parent("visitor@test.com", userId);

        var notifications = BlogCommentNotifications.Plan(Comment("visitor@test.com", userId), parent, AuthorEmail);

        var notification = Assert.Single(notifications);
        Assert.Equal(BlogCommentNotificationKind.NewCommentForAuthor, notification.Kind);
    }

    [Fact]
    public void AuthorCommenting_GetsNoSelfNotification()
    {
        var notifications = BlogCommentNotifications.Plan(Comment("author@kalandra.local"), parent: null, AuthorEmail);

        Assert.Empty(notifications);
    }

    [Fact]
    public void ReplyToAuthorsComment_SendsTheAuthorOnlyOneEmail()
    {
        var parent = Parent("author@kalandra.local");

        var notifications = BlogCommentNotifications.Plan(Comment("visitor@test.com"), parent, AuthorEmail);

        var notification = Assert.Single(notifications);
        Assert.Equal(BlogCommentNotificationKind.NewCommentForAuthor, notification.Kind);
    }

    [Fact]
    public void ReplyToDeletedParent_DoesNotNotifyItsAuthor()
    {
        // Reachable only through a race (parent deleted while the reply was in flight) —
        // the tombstoned author still must not be emailed.
        var parent = Parent("parent@test.com", isDeleted: true);

        var notifications = BlogCommentNotifications.Plan(Comment("visitor@test.com"), parent, AuthorEmail);

        var notification = Assert.Single(notifications);
        Assert.Equal(BlogCommentNotificationKind.NewCommentForAuthor, notification.Kind);
    }

    [Fact]
    public void AuthorEmailComparison_IsCaseInsensitive()
    {
        var notifications = BlogCommentNotifications.Plan(Comment("AUTHOR@KALANDRA.LOCAL"), parent: null, AuthorEmail);

        Assert.Empty(notifications);
    }
}
