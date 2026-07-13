using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Infrastructure.Email;
using Marten;
using Marten.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Blog.Notifications;

/// <summary>
/// Sends the blog-comment notification emails as comments are committed: the author hears about every
/// comment, and a reply also notifies the parent comment's author. The async daemon delivers each
/// event here at least once, so a comment is never stored without its notifications following.
/// </summary>
// IServiceProvider, not IDocumentStore: Marten resolves the subscription while it is still building the
// store, so a direct IDocumentStore dependency deadlocks the container. Resolve the store lazily at run time.
public class BlogCommentNotificationSubscription(
    IServiceProvider services,
    IEmailSender emailSender,
    IBlogPostCatalog postCatalog,
    BlogNotificationsConfig notificationsConfig,
    TimeProvider timeProvider) : SubscriptionBase
{
    private const string SiteUrl = "https://www.kalandra.tech";

    public override async Task<IChangeListener> ProcessEventsAsync(
        EventRange page, ISubscriptionController controller, IDocumentOperations operations, CancellationToken cancellationToken)
    {
        foreach (var e in page.Events)
        {
            if (e.Data is BlogCommentPosted comment)
                await NotifyCommentAsync(e.StreamId, comment, operations, cancellationToken);
        }

        return NullChangeListener.Instance;
    }

    private async Task NotifyCommentAsync(Guid commentsStreamId, BlogCommentPosted comment, IDocumentOperations operations, CancellationToken ct)
    {
        // The event only carries its comment stream id; the catalog maps that back to the post's slug.
        if (postCatalog.FindByCommentsStreamId(commentsStreamId) is not { } post)
            return;

        BlogPostComment? parent = null;
        if (comment.ParentCommentId is { } parentId)
        {
            var comments = await operations.Events.AggregateStreamAsync<BlogPostComments>(commentsStreamId, token: ct)
                ?? new BlogPostComments();
            parent = comments.Comments.FirstOrDefault(c => c.CommentId == parentId);
        }

        foreach (var notification in BlogCommentNotifications.Plan(comment, parent, notificationsConfig.AuthorEmail))
            await DeliverAsync($"blog-comment:{comment.CommentId}:{notification.Recipient.Address}", BuildEmail(notification, comment, post.Slug), ct);
    }

    // Its own transaction, committed the moment a send lands, so a page retry re-sends only what didn't.
    private async Task DeliverAsync(string dedupeKey, EmailMessage email, CancellationToken ct)
    {
        await using var session = services.GetRequiredService<IDocumentStore>().LightweightSession();
        if (await session.LoadAsync<BlogNotificationSent>(dedupeKey, ct) is not null)
            return;

        await emailSender.SendAsync(email, ct);
        session.Store(new BlogNotificationSent(dedupeKey, timeProvider.GetUtcNow()));
        await session.SaveChangesAsync(ct);
    }

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
