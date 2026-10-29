using StrongTypes;

namespace Kalandra.JobOffers.Events;

// JobOfferId is carried explicitly so a consumer that only sees the event, like the notification subscription, can load the offer.
public record JobOfferCommentAdded(
    Guid JobOfferId,
    Guid CommentId,
    Guid UserId,
    Email UserEmail,
    NonEmptyString UserName,
    NonEmptyString Content,
    DateTimeOffset Timestamp);
