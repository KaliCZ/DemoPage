using Kalandra.Api.Features.JobOffers.Entities;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Comments;

public class AddCommentHandler(IDocumentSession session, TimeProvider timeProvider)
{
    public async Task<(bool Success, string? Error)> HandleAsync(
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

        var (success, error, commentAdded) = offer.AddComment(
            userId,
            userEmail,
            userName,
            request.Content,
            isAdmin,
            timeProvider.GetUtcNow());

        if (!success || commentAdded == null)
            return (false, error);

        stream.AppendOne(commentAdded);
        await session.SaveChangesAsync(ct);
        return (true, null);
    }
}
