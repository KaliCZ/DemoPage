namespace Kalandra.JobOffers.Events;

public record JobOfferCommentAdded(
    Guid CommentId,
    Guid UserId,
    string UserEmail,
    string UserName,
    string Content,
    DateTimeOffset Timestamp);
