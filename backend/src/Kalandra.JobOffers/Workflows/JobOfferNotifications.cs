using System.Net.Mail;
using Kalandra.JobOffers.Commands;
using Kalandra.JobOffers.Events;

namespace Kalandra.JobOffers.Workflows;

public enum JobOfferCommentNotificationKind
{
    NewCommentForOwner,
    NewCommentForOfferAuthor,
}

public record JobOfferCommentNotification(MailAddress Recipient, JobOfferCommentNotificationKind Kind);

/// <summary>Serializable form for crossing the activity boundary — MailAddress doesn't round-trip through the Temporal payload converter.</summary>
public record PlannedJobOfferCommentNotification(string RecipientEmail, JobOfferCommentNotificationKind Kind);

public static class JobOfferNotifications
{
    /// <summary>The site owner hears about every submitted offer — unless they submitted it themselves.</summary>
    public static IReadOnlyList<MailAddress> PlanSubmitted(JobOfferSubmitted submitted, MailAddress ownerEmail) =>
        SameAddress(submitted.UserEmail.Value, ownerEmail) ? [] : [ownerEmail];

    /// <summary>
    /// The site owner and the offer's author both hear about every comment — but
    /// nobody is notified about their own comment, and nobody gets two emails for one.
    /// </summary>
    public static IReadOnlyList<JobOfferCommentNotification> PlanComment(StoredJobOfferComment stored, MailAddress ownerEmail)
    {
        var notifications = new List<JobOfferCommentNotification>();
        var comment = stored.Comment;

        // Event/entity emails are StrongTypes.Email (a serialization boundary); .Value is the MailAddress used in code.
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
