using System.Net.Mail;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Kalandra.Infrastructure.Email;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Events;
using Marten;
using Marten.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.JobOffers.Notifications;

/// <summary>
/// The async daemon delivers each event at least once, hence the per-recipient dedupe in DeliverAsync.
/// </summary>
// IServiceProvider, not IDocumentStore: Marten resolves this while building the store, so injecting the store deadlocks the container.
public class JobOfferNotificationSubscription(
    IServiceProvider services,
    IEmailSender emailSender,
    JobOffersNotificationsConfig notificationsConfig,
    TimeProvider timeProvider) : SubscriptionBase
{
    private const string SiteUrl = "https://www.kalandra.tech";

    public override async Task<IChangeListener> ProcessEventsAsync(
        EventRange page, ISubscriptionController controller, IDocumentOperations operations, CancellationToken cancellationToken)
    {
        foreach (var e in page.Events)
        {
            switch (e.Data)
            {
                case JobOfferSubmitted submitted:
                    await NotifySubmittedAsync(e.StreamId, submitted, cancellationToken);
                    break;
                case JobOfferCommentAdded comment:
                    await NotifyCommentAsync(comment, operations, cancellationToken);
                    break;
            }
        }

        return NullChangeListener.Instance;
    }

    private async Task NotifySubmittedAsync(Guid jobOfferId, JobOfferSubmitted submitted, CancellationToken ct)
    {
        foreach (var recipient in JobOfferNotifications.PlanSubmitted(submitted, notificationsConfig.OwnerEmail))
            await DeliverAsync($"offer-submitted:{jobOfferId}:{recipient.Address}", BuildSubmittedEmail(recipient, jobOfferId, submitted), ct);
    }

    private async Task NotifyCommentAsync(JobOfferCommentAdded comment, IDocumentOperations operations, CancellationToken ct)
    {
        var offer = await operations.LoadAsync<JobOffer>(comment.JobOfferId, ct);
        if (offer is null)
            return;

        var stored = new StoredJobOfferComment(
            JobOfferId: comment.JobOfferId,
            Comment: comment,
            OfferAuthorUserId: offer.UserId,
            OfferAuthorEmail: offer.UserEmail,
            CompanyName: offer.CompanyName,
            JobTitle: offer.JobTitle);

        foreach (var notification in JobOfferNotifications.PlanComment(stored, notificationsConfig.OwnerEmail))
            await DeliverAsync($"offer-comment:{comment.CommentId}:{notification.Recipient.Address}", BuildCommentEmail(notification, stored), ct);
    }

    // Its own transaction, committed the moment a send lands, so a page retry re-sends only what didn't.
    private async Task DeliverAsync(string dedupeKey, EmailMessage email, CancellationToken ct)
    {
        await using var session = services.GetRequiredService<IDocumentStore>().LightweightSession();
        if (await session.LoadAsync<JobOfferNotificationSent>(dedupeKey, ct) is not null)
            return;

        await emailSender.SendAsync(email, ct);
        session.Store(new JobOfferNotificationSent(dedupeKey, timeProvider.GetUtcNow()));
        await session.SaveChangesAsync(ct);
    }

    private static EmailMessage BuildSubmittedEmail(MailAddress recipient, Guid jobOfferId, JobOfferSubmitted submitted)
    {
        var offerLabel = $"{submitted.JobTitle.Value} at {submitted.CompanyName.Value}";
        var subject = $"New job offer: {offerLabel}";
        var body = $"{submitted.ContactName.Value} ({submitted.ContactEmail.Value.Address}) submitted a job offer: " +
            $"{offerLabel}.\n\n{submitted.Description.Value}\n\n{AdminOfferUrl(jobOfferId)}";

        return new EmailMessage(recipient, subject.ToNonEmpty(), body.ToNonEmpty());
    }

    private static EmailMessage BuildCommentEmail(JobOfferCommentNotification notification, StoredJobOfferComment stored)
    {
        var offerLabel = $"{stored.JobTitle.Value} at {stored.CompanyName.Value}";
        var comment = stored.Comment;
        var (subject, body) = notification.Kind switch
        {
            JobOfferCommentNotificationKind.NewCommentForOwner => (
                $"New comment on job offer: {offerLabel}",
                $"{comment.UserName.Value} commented on the job offer {offerLabel}:\n\n{comment.Content.Value}\n\n{AdminOfferUrl(stored.JobOfferId)}"),
            JobOfferCommentNotificationKind.NewCommentForOfferAuthor => (
                $"New comment on your job offer: {offerLabel}",
                $"{comment.UserName.Value} commented on your job offer {offerLabel}:\n\n{comment.Content.Value}\n\n{AuthorOfferUrl(stored.JobOfferId)}"),
        };

        return new EmailMessage(notification.Recipient, subject.ToNonEmpty(), body.ToNonEmpty());
    }

    private static string AdminOfferUrl(Guid jobOfferId) => $"{SiteUrl}/admin/job-offers/detail?id={jobOfferId}";

    private static string AuthorOfferUrl(Guid jobOfferId) => $"{SiteUrl}/job-offers/detail?id={jobOfferId}";
}
