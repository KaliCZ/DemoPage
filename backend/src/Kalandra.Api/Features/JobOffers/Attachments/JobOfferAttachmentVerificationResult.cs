using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.Attachments;

public record JobOfferAttachmentVerificationResult(
    bool Success,
    string? Error,
    IReadOnlyList<AttachmentInfo> Attachments)
{
    public static JobOfferAttachmentVerificationResult Verified(IReadOnlyList<AttachmentInfo> attachments) =>
        new(true, null, attachments);

    public static JobOfferAttachmentVerificationResult Failed(string error) =>
        new(false, error, []);
}
