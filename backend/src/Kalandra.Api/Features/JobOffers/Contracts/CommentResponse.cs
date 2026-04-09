using Kalandra.JobOffers.Events;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record CommentResponse(
    Guid Id,
    Guid UserId,
    string UserEmail,
    string UserName,
    string Content,
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
    List<CommentResponse> Comments,
    Dictionary<Guid, Uri> Avatars);
