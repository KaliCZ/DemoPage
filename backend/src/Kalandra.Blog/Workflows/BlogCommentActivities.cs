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
        var command = new StoreBlogCommentCommand(ParseSlug(input.Slug), input.Comment);
        var result = await storeHandler.HandleAsync(command, ct);
        return new StoreBlogCommentOutcome(result.Success, result.Error);
    }

    [Activity]
    public async Task SendCommentNotificationsAsync(BlogCommentWorkflowInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var slug = ParseSlug(input.Slug);

        BlogPostComment? parent = null;
        if (input.Comment.ParentCommentId is { } parentId)
        {
            var comments = await commentsHandler.HandleAsync(new GetBlogCommentsQuery(slug), ct);
            parent = comments.Comments.FirstOrDefault(c => c.CommentId == parentId);
        }

        foreach (var notification in BlogCommentNotifications.Plan(input.Comment, parent, notificationsConfig.AuthorEmail))
        {
            await emailSender.SendAsync(BuildEmail(notification, input.Comment, slug.Value), ct);
        }
    }

    private static BlogPostSlug ParseSlug(string slug) =>
        BlogPostSlug.TryCreate(slug)
            ?? throw new InvalidOperationException($"Workflow input carried an invalid slug \"{slug}\"");

    private static EmailMessage BuildEmail(BlogCommentNotification notification, BlogCommentPosted comment, string slug)
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

        return new EmailMessage(notification.Recipient, subject.ToNonEmpty(), body.ToNonEmpty());
    }
}
