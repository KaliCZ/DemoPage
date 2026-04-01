using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.Attachments;

public interface IJobOfferAttachmentVerifier
{
    Task<Try<IReadOnlyList<AttachmentInfo>, AttachmentVerificationError>> VerifyAsync(
        Guid jobOfferId,
        string userId,
        IReadOnlyList<AttachmentInfo>? attachments,
        CancellationToken ct);
}
