using Kalandra.Api.Features.JobOffers.Entities;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Comments;

public class AddCommentHandler(IDocumentSession session, TimeProvider timeProvider)
{
    public async Task<Try<Unit, AddJobOfferCommentError>> HandleAsync(
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
            return Try.Error<Unit, AddJobOfferCommentError>(AddJobOfferCommentError.NotFound);

        var result = offer.AddComment(
            userId: userId,
            userEmail: userEmail,
            userName: userName,
            content: request.Content,
            isAdmin: isAdmin,
            timestamp: timeProvider.GetUtcNow());

        if (result.IsError)
            return result.Map<Unit>(_ => Unit.Value);

        stream.AppendOne(result.Success.Get((Unit _) => new InvalidOperationException()));
        await session.SaveChangesAsync(ct);
        return Try.Success<Unit, AddJobOfferCommentError>(Unit.Value);
    }
}
