using StrongTypes;

namespace Kalandra.JobOffers.Events;

public record JobOfferCommentAdded(
    Guid CommentId,
    Guid UserId,
    Email UserEmail,
    NonEmptyString UserName,
    NonEmptyString Content,
    DateTimeOffset Timestamp);
