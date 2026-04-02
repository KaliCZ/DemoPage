namespace Kalandra.JobOffers.Events;

public record JobOfferCommentAdded(
    Guid CommentId,
    string UserId,
    string UserEmail,
    string UserName,
    string Content,
    DateTimeOffset Timestamp);
