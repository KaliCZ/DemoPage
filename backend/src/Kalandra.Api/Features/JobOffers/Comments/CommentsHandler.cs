using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Comments;

public class CommentsHandler(IDocumentSession session)
{
    public async Task<(bool Success, string? Error)> AddCommentAsync(
        Guid jobOfferId,
        AddCommentRequest request,
        string userId,
        string userEmail,
        string userName,
        bool isAdmin,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(jobOfferId, ct);
        var offer = stream.Aggregate;
        if (offer == null)
            return (false, "Not found");

        // Only owner or admin can comment
        if (!isAdmin && offer.UserId != userId)
            return (false, "Not authorized");

        if (string.IsNullOrWhiteSpace(request.Content))
            return (false, "Content is required");

        var commentAdded = new JobOfferCommentAdded(
            CommentId: Guid.NewGuid(),
            UserId: userId,
            UserEmail: userEmail,
            UserName: userName,
            Content: request.Content.Trim(),
            Timestamp: DateTimeOffset.UtcNow);

        stream.AppendOne(commentAdded);
        await session.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<ListCommentsResponse?> ListCommentsAsync(
        Guid jobOfferId,
        string? requesterUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(jobOfferId, ct);
        if (offer == null)
            return null;

        if (!isAdmin && offer.UserId != requesterUserId)
            return null;

        var events = await session.Events.FetchStreamAsync(jobOfferId, token: ct);

        var comments = events
            .Where(e => e.Data is JobOfferCommentAdded)
            .Select(e => (JobOfferCommentAdded)e.Data)
            .Select(c => new CommentResponse(
                c.CommentId,
                c.UserId,
                c.UserEmail,
                c.UserName,
                c.Content,
                c.Timestamp))
            .ToList();

        return new ListCommentsResponse(comments);
    }
}
