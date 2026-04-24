namespace Kalandra.JobOffers.Events;

public record JobOfferCommentAdded(
    Guid CommentId,
    Guid UserId,
    NonEmptyString UserEmail,
    NonEmptyString UserName,
    NonEmptyString Content,
    DateTimeOffset Timestamp);
