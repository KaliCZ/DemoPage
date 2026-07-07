using System.Net.Mail;
using Kalandra.JobOffers.Commands;
using Kalandra.JobOffers.Events;
using Kalandra.JobOffers.Workflows;

namespace Kalandra.JobOffers.Tests;

public class JobOfferNotificationsTests
{
    private static readonly MailAddress OwnerEmail = new("owner@kalandra.local");
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private static JobOfferSubmitted Submitted(string userEmail) => new(
        UserId: Guid.NewGuid(),
        UserEmail: Email.Create(userEmail),
        CompanyName: "Acme Corp".ToNonEmpty(),
        ContactName: "John Doe".ToNonEmpty(),
        ContactEmail: Email.Create("john@acme.com"),
        JobTitle: "Senior Developer".ToNonEmpty(),
        Description: "A role.".ToNonEmpty(),
        SalaryRange: null,
        Location: null,
        IsRemote: true,
        AdditionalNotes: null,
        Attachments: [],
        Timestamp: Now);

    private static StoredJobOfferComment Comment(
        string commenterEmail,
        Guid? commenterUserId = null,
        Guid? offerAuthorUserId = null,
        string offerAuthorEmail = "offer-author@test.com") => new(
        JobOfferId: Guid.NewGuid(),
        Comment: new JobOfferCommentAdded(
            CommentId: Guid.NewGuid(),
            UserId: commenterUserId ?? Guid.NewGuid(),
            UserEmail: Email.Create(commenterEmail),
            UserName: "Commenter".ToNonEmpty(),
            Content: "Hello".ToNonEmpty(),
            Timestamp: Now),
        OfferAuthorUserId: offerAuthorUserId ?? Guid.NewGuid(),
        OfferAuthorEmail: Email.Create(offerAuthorEmail),
        CompanyName: "Acme Corp".ToNonEmpty(),
        JobTitle: "Senior Developer".ToNonEmpty());

    [Fact]
    public void SubmittedOffer_NotifiesTheOwner()
    {
        var recipients = JobOfferNotifications.PlanSubmitted(Submitted("visitor@test.com"), OwnerEmail);

        var recipient = Assert.Single(recipients);
        Assert.Equal("owner@kalandra.local", recipient.Address);
    }

    [Fact]
    public void OwnersOwnOffer_SendsNoNotification()
    {
        Assert.Empty(JobOfferNotifications.PlanSubmitted(Submitted("owner@kalandra.local"), OwnerEmail));
    }

    [Fact]
    public void SubmittedOwnerComparison_IsCaseInsensitive()
    {
        Assert.Empty(JobOfferNotifications.PlanSubmitted(Submitted("OWNER@KALANDRA.LOCAL"), OwnerEmail));
    }

    [Fact]
    public void OfferAuthorsComment_NotifiesOnlyTheOwner()
    {
        var authorId = Guid.NewGuid();
        var stored = Comment("offer-author@test.com", commenterUserId: authorId, offerAuthorUserId: authorId);

        var notifications = JobOfferNotifications.PlanComment(stored, OwnerEmail);

        var notification = Assert.Single(notifications);
        Assert.Equal(JobOfferCommentNotificationKind.NewCommentForOwner, notification.Kind);
        Assert.Equal("owner@kalandra.local", notification.Recipient.Address);
    }

    [Fact]
    public void OwnersComment_NotifiesOnlyTheOfferAuthor()
    {
        var stored = Comment("owner@kalandra.local");

        var notifications = JobOfferNotifications.PlanComment(stored, OwnerEmail);

        var notification = Assert.Single(notifications);
        Assert.Equal(JobOfferCommentNotificationKind.NewCommentForOfferAuthor, notification.Kind);
        Assert.Equal("offer-author@test.com", notification.Recipient.Address);
    }

    [Fact]
    public void OtherAdminsComment_NotifiesOwnerAndOfferAuthor()
    {
        var stored = Comment("second-admin@test.com");

        var notifications = JobOfferNotifications.PlanComment(stored, OwnerEmail);

        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, n =>
            n.Kind == JobOfferCommentNotificationKind.NewCommentForOwner && n.Recipient.Address == "owner@kalandra.local");
        Assert.Contains(notifications, n =>
            n.Kind == JobOfferCommentNotificationKind.NewCommentForOfferAuthor && n.Recipient.Address == "offer-author@test.com");
    }

    [Fact]
    public void CommentOnOwnersOwnOffer_SendsTheOwnerOnlyOneEmail()
    {
        var stored = Comment("second-admin@test.com", offerAuthorEmail: "owner@kalandra.local");

        var notifications = JobOfferNotifications.PlanComment(stored, OwnerEmail);

        var notification = Assert.Single(notifications);
        Assert.Equal(JobOfferCommentNotificationKind.NewCommentForOwner, notification.Kind);
    }

    [Fact]
    public void CommentOwnerComparison_IsCaseInsensitive()
    {
        var stored = Comment("OWNER@KALANDRA.LOCAL");

        var notifications = JobOfferNotifications.PlanComment(stored, OwnerEmail);

        var notification = Assert.Single(notifications);
        Assert.Equal(JobOfferCommentNotificationKind.NewCommentForOfferAuthor, notification.Kind);
    }
}
