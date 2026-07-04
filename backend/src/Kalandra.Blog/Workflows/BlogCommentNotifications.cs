using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;

namespace Kalandra.Blog.Workflows;

public enum BlogCommentNotificationKind
{
    NewCommentForAuthor,
    ReplyToYourComment,
}

public record BlogCommentNotification(Email Recipient, BlogCommentNotificationKind Kind);

public static class BlogCommentNotifications
{
    /// <summary>
    /// The blog author hears about every comment and a reply also notifies the
    /// parent comment's author — but nobody is notified about their own comment,
    /// and nobody gets two emails for one.
    /// </summary>
    public static IReadOnlyList<BlogCommentNotification> Plan(
        BlogCommentPosted comment, BlogPostComment? parent, Email authorEmail)
    {
        var notifications = new List<BlogCommentNotification>();

        if (!SameAddress(comment.UserEmail, authorEmail))
            notifications.Add(new BlogCommentNotification(authorEmail, BlogCommentNotificationKind.NewCommentForAuthor));

        if (parent is { IsDeleted: false }
            && parent.UserId != comment.UserId
            && !SameAddress(parent.UserEmail, authorEmail))
        {
            notifications.Add(new BlogCommentNotification(parent.UserEmail, BlogCommentNotificationKind.ReplyToYourComment));
        }

        return notifications;
    }

    private static bool SameAddress(Email left, Email right) =>
        string.Equals(left.Value.Address, right.Value.Address, StringComparison.OrdinalIgnoreCase);
}
