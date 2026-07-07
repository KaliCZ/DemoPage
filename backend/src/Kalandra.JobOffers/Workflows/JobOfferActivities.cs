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
    public async Task StoreJobOfferAsync(JobOfferSubmittedWorkflowInput input)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        await storeOfferHandler.StoreAndSave(new StoreJobOfferCommand(input.JobOfferId, input.Submitted), ct);
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
        await emailSender.SendAsync(BuildSubmittedEmail(recipientEmail, input.Submitted), ct);
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

    private static EmailMessage BuildSubmittedEmail(string recipientEmail, JobOfferSubmitted submitted)
    {
        var offerLabel = $"{submitted.JobTitle.Value} at {submitted.CompanyName.Value}";
        var subject = $"New job offer: {offerLabel}";
        var body = $"{submitted.ContactName.Value} ({submitted.ContactEmail.Value.Address}) submitted a job offer: " +
            $"{offerLabel}.\n\n{submitted.Description.Value}\n\n{SiteUrl}/admin/job-offers";

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
                $"{comment.UserName.Value} commented on the job offer {offerLabel}:\n\n{comment.Content.Value}\n\n{SiteUrl}/admin/job-offers"),
            JobOfferCommentNotificationKind.NewCommentForOfferAuthor => (
                $"New comment on your job offer: {offerLabel}",
                $"{comment.UserName.Value} commented on your job offer {offerLabel}:\n\n{comment.Content.Value}\n\n{SiteUrl}/job-offers"),
        };

        return new EmailMessage(new MailAddress(notification.RecipientEmail), subject.ToNonEmpty(), body.ToNonEmpty());
    }
}
