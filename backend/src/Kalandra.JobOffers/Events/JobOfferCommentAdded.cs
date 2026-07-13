using StrongTypes;

namespace Kalandra.JobOffers.Events;

// JobOfferId is carried on the event, not just implied by the derived comment stream, so a
// consumer that only sees the event (the notification subscription) can load the parent offer.
public record JobOfferCommentAdded(
    Guid JobOfferId,
    Guid CommentId,
    Guid UserId,
    Email UserEmail,
    NonEmptyString UserName,
    NonEmptyString Content,
    DateTimeOffset Timestamp);
