using System.Net.Mail;
using Kalandra.Infrastructure.Email;
using Kalandra.JobOffers.Commands;
using Kalandra.JobOffers.Events;
using StrongTypes;
using Temporalio.Activities;

namespace Kalandra.JobOffers.Workflows;

public class JobOfferActivities(
    StoreJobOfferHandler storeOfferHandler,
    StoreJobOfferCommentHandler storeCommentHandler,
    IEmailSender emailSender,
    JobOffersNotificationsConfig notificationsConfig)
{
    private const string SiteUrl = "https://www.kalandra.tech";

    [Activity]
    public async Task<StoreJobOfferOutcome> StoreJobOfferAsync(JobOfferSubmittedWorkflowInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var result = await storeOfferHandler.StoreAndSave(new StoreJobOfferCommand(input.JobOfferId, input.Submitted), ct);
        return new StoreJobOfferOutcome(result.Error);
    }

    [Activity]
    public IReadOnlyList<string> PlanSubmittedNotifications(JobOfferSubmittedWorkflowInput input) =>
        JobOfferNotifications.PlanSubmitted(input.Submitted, notificationsConfig.OwnerEmail)
            .Select(recipient => recipient.Address)
            .ToList();

    [Activity]
    public async Task SendSubmittedNotificationAsync(string recipientEmail, JobOfferSubmittedWorkflowInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        await emailSender.SendAsync(BuildSubmittedEmail(recipientEmail, input.JobOfferId, input.Submitted), ct);
    }

    [Activity]
    public async Task<StoreJobOfferCommentOutcome> StoreCommentAsync(JobOfferCommentWorkflowInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var command = new StoreJobOfferCommentCommand(input.JobOfferId, input.Comment, input.CommenterIsAdmin);
        var result = await storeCommentHandler.StoreAndSave(command, ct);
        return new StoreJobOfferCommentOutcome(result.Success, result.Error);
    }

    [Activity]
    public IReadOnlyList<PlannedJobOfferCommentNotification> PlanCommentNotifications(StoredJobOfferComment stored) =>
        JobOfferNotifications.PlanComment(stored, notificationsConfig.OwnerEmail)
            .Select(notification => new PlannedJobOfferCommentNotification(notification.Recipient.Address, notification.Kind))
            .ToList();

    [Activity]
    public async Task SendCommentNotificationAsync(PlannedJobOfferCommentNotification notification, StoredJobOfferComment stored)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        await emailSender.SendAsync(BuildCommentEmail(notification, stored), ct);
    }

    private static EmailMessage BuildSubmittedEmail(string recipientEmail, Guid jobOfferId, JobOfferSubmitted submitted)
    {
        var offerLabel = $"{submitted.JobTitle.Value} at {submitted.CompanyName.Value}";
        var subject = $"New job offer: {offerLabel}";
        var body = $"{submitted.ContactName.Value} ({submitted.ContactEmail.Value.Address}) submitted a job offer: " +
            $"{offerLabel}.\n\n{submitted.Description.Value}\n\n{AdminOfferUrl(jobOfferId)}";

        return new EmailMessage(new MailAddress(recipientEmail), subject.ToNonEmpty(), body.ToNonEmpty());
    }

    private static EmailMessage BuildCommentEmail(PlannedJobOfferCommentNotification notification, StoredJobOfferComment stored)
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

        return new EmailMessage(new MailAddress(notification.RecipientEmail), subject.ToNonEmpty(), body.ToNonEmpty());
    }

    // Workflows in flight across the deploy that added JobOfferId replay it as Guid.Empty — fall back to the list page.
    private static string AdminOfferUrl(Guid jobOfferId) =>
        jobOfferId == Guid.Empty ? $"{SiteUrl}/admin/job-offers" : $"{SiteUrl}/admin/job-offers/detail?id={jobOfferId}";

    private static string AuthorOfferUrl(Guid jobOfferId) =>
        jobOfferId == Guid.Empty ? $"{SiteUrl}/job-offers" : $"{SiteUrl}/job-offers/detail?id={jobOfferId}";
}
