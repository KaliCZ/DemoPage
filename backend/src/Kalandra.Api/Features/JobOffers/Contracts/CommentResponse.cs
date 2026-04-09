namespace Kalandra.Api.Features.JobOffers.Contracts;

public record CommentResponse(
    Guid Id,
    Guid UserId,
    string UserEmail,
    string UserName,
    string Content,
    DateTimeOffset CreatedAt);

public record ListCommentsResponse(List<CommentResponse> Comments);
