using System.Net.Mail;
using Kalandra.JobOffers.Events;
using StrongTypes;

namespace Kalandra.JobOffers.Notifications;

public enum JobOfferCommentNotificationKind
{
    NewCommentForOwner,
    NewCommentForOfferAuthor,
}

public record JobOfferCommentNotification(MailAddress Recipient, JobOfferCommentNotificationKind Kind);

public record StoredJobOfferComment(
    Guid JobOfferId,
    JobOfferCommentAdded Comment,
    Guid OfferAuthorUserId,
    Email OfferAuthorEmail,
    NonEmptyString CompanyName,
    NonEmptyString JobTitle);

public static class JobOfferNotifications
{
    public static IReadOnlyList<MailAddress> PlanSubmitted(JobOfferSubmitted submitted, MailAddress ownerEmail) =>
        SameAddress(submitted.UserEmail.Value, ownerEmail) ? [] : [ownerEmail];

    /// <summary>Nobody is notified about their own comment, and nobody gets two emails for one.</summary>
    public static IReadOnlyList<JobOfferCommentNotification> PlanComment(StoredJobOfferComment stored, MailAddress ownerEmail)
    {
        var notifications = new List<JobOfferCommentNotification>();
        var comment = stored.Comment;

        if (!SameAddress(comment.UserEmail.Value, ownerEmail))
            notifications.Add(new JobOfferCommentNotification(ownerEmail, JobOfferCommentNotificationKind.NewCommentForOwner));

        if (stored.OfferAuthorUserId != comment.UserId && !SameAddress(stored.OfferAuthorEmail.Value, ownerEmail))
        {
            notifications.Add(new JobOfferCommentNotification(
                stored.OfferAuthorEmail.Value, JobOfferCommentNotificationKind.NewCommentForOfferAuthor));
        }

        return notifications;
    }

    private static bool SameAddress(MailAddress left, MailAddress right) =>
        string.Equals(left.Address, right.Address, StringComparison.OrdinalIgnoreCase);
}
