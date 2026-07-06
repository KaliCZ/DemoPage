using System.Net.Mail;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;

namespace Kalandra.Blog.Workflows;

public enum BlogCommentNotificationKind
{
    NewCommentForAuthor,
    ReplyToYourComment,
}

public record BlogCommentNotification(MailAddress Recipient, BlogCommentNotificationKind Kind);

public static class BlogCommentNotifications
{
    /// <summary>
    /// The blog author hears about every comment and a reply also notifies the
    /// parent comment's author — but nobody is notified about their own comment,
    /// and nobody gets two emails for one.
    /// </summary>
    public static IReadOnlyList<BlogCommentNotification> Plan(
        BlogCommentPosted comment, BlogPostComment? parent, MailAddress authorEmail)
    {
        var notifications = new List<BlogCommentNotification>();

        // Event/entity emails are StrongTypes.Email (a serialization boundary); .Value is the MailAddress used in code.
        if (!SameAddress(comment.UserEmail.Value, authorEmail))
            notifications.Add(new BlogCommentNotification(authorEmail, BlogCommentNotificationKind.NewCommentForAuthor));

        if (parent is { IsDeleted: false }
            && parent.UserId != comment.UserId
            && !SameAddress(parent.UserEmail.Value, authorEmail))
        {
            notifications.Add(new BlogCommentNotification(parent.UserEmail.Value, BlogCommentNotificationKind.ReplyToYourComment));
        }

        return notifications;
    }

    private static bool SameAddress(MailAddress left, MailAddress right) =>
        string.Equals(left.Address, right.Address, StringComparison.OrdinalIgnoreCase);
}
