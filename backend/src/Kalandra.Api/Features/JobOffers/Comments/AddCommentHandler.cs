using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
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
        var offer = await session.LoadAsync<JobOffer>(jobOfferId, ct);
        if (offer == null)
            return Try.Error<Unit, AddJobOfferCommentError>(AddJobOfferCommentError.NotFound);

        if (!isAdmin && offer.UserId != userId)
            return Try.Error<Unit, AddJobOfferCommentError>(AddJobOfferCommentError.NotAuthorized);

        if (string.IsNullOrWhiteSpace(request.Content))
            return Try.Error<Unit, AddJobOfferCommentError>(AddJobOfferCommentError.ContentRequired);

        var commentEvent = new JobOfferCommentAdded(
            CommentId: Guid.NewGuid(),
            UserId: userId,
            UserEmail: userEmail,
            UserName: userName,
            Content: request.Content.Trim(),
            Timestamp: timeProvider.GetUtcNow());

        session.Events.Append(CommentStreamId(jobOfferId), commentEvent);
        await session.SaveChangesAsync(ct);
        return Try.Success<Unit, AddJobOfferCommentError>(Unit.Value);
    }

    /// <summary>
    /// Deterministic stream ID for a job offer's comments, separate from the main job offer stream.
    /// Uses UUID v5 (SHA-1 name-based) to derive a stable, collision-free ID from the job offer ID.
    /// </summary>
    public static Guid CommentStreamId(Guid jobOfferId)
    {
        var namespaceBytes = CommentStreamNamespace.ToByteArray();
        var nameBytes = jobOfferId.ToByteArray();

        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);

        var hash = System.Security.Cryptography.SHA1.HashData(input);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant RFC 4122

        return new Guid(hash.AsSpan(0, 16));
    }

    private static readonly Guid CommentStreamNamespace =
        Guid.Parse("b6f0a5c2-7e3d-4f1a-9c8b-2d5e6f7a8b9c");
}
