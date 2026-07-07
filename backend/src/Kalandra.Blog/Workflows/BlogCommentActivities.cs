using System.Net.Mail;
using Kalandra.Blog.Commands;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Blog.Queries;
using Kalandra.Infrastructure.Email;
using Temporalio.Activities;

namespace Kalandra.Blog.Workflows;

public class BlogCommentActivities(
    StoreBlogCommentHandler storeHandler,
    GetBlogCommentsHandler commentsHandler,
    IEmailSender emailSender,
    BlogNotificationsConfig notificationsConfig)
{
    private const string SiteUrl = "https://www.kalandra.tech";

    [Activity]
    public async Task<StoreBlogCommentOutcome> StoreCommentAsync(BlogCommentWorkflowInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var command = new StoreBlogCommentCommand(input.CommentsStreamId, input.Comment);
        var result = await storeHandler.StoreAndSave(command, ct);
        return new StoreBlogCommentOutcome(result.Success, result.Error);
    }

    [Activity]
    public async Task<IReadOnlyList<PlannedBlogCommentNotification>> PlanCommentNotificationsAsync(BlogCommentWorkflowInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;

        BlogPostComment? parent = null;
        if (input.Comment.ParentCommentId is { } parentId)
        {
            var comments = await commentsHandler.Get(new GetBlogCommentsQuery(input.CommentsStreamId), ct);
            parent = comments.Comments.FirstOrDefault(c => c.CommentId == parentId);
        }

        return BlogCommentNotifications.Plan(input.Comment, parent, notificationsConfig.AuthorEmail)
            .Select(notification => new PlannedBlogCommentNotification(notification.Recipient.Address, notification.Kind))
            .ToList();
    }

    [Activity]
    public async Task SendCommentNotificationAsync(PlannedBlogCommentNotification notification, BlogCommentWorkflowInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        await emailSender.SendAsync(BuildEmail(notification, input.Comment, input.Slug), ct);
    }

    private static EmailMessage BuildEmail(PlannedBlogCommentNotification notification, BlogCommentPosted comment, string slug)
    {
        var postUrl = $"{SiteUrl}/blog/{slug}";
        var (subject, body) = notification.Kind switch
        {
            BlogCommentNotificationKind.NewCommentForAuthor => (
                $"New comment on {slug}",
                $"{comment.AuthorDisplayName.Value} commented on {postUrl}:\n\n{comment.Content.Value}"),
            BlogCommentNotificationKind.ReplyToYourComment => (
                $"New reply to your comment on {slug}",
                $"{comment.AuthorDisplayName.Value} replied to your comment on {postUrl}:\n\n{comment.Content.Value}"),
        };

        return new EmailMessage(new MailAddress(notification.RecipientEmail), subject.ToNonEmpty(), body.ToNonEmpty());
    }
}
