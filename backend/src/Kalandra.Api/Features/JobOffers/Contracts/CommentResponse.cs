using Kalandra.JobOffers.Events;
using StrongTypes;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record CommentResponse(
    Guid Id,
    Guid UserId,
    Email UserEmail,
    NonEmptyString UserName,
    NonEmptyString Content,
    DateTimeOffset CreatedAt)
{
    public static CommentResponse Serialize(JobOfferCommentAdded comment) => new(
        Id: comment.CommentId,
        UserId: comment.UserId,
        UserEmail: comment.UserEmail,
        UserName: comment.UserName,
        Content: comment.Content,
        CreatedAt: comment.Timestamp);
}

public record ListCommentsResponse(
    IEnumerable<CommentResponse> Comments);
