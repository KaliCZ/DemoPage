using Kalandra.Api.Features.JobOffers.Attachments;
using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Tests.Helpers;

public class FakeJobOfferAttachmentVerifier : IJobOfferAttachmentVerifier
{
    public Task<JobOfferAttachmentVerificationResult> VerifyAsync(
        Guid jobOfferId,
        string userId,
        IReadOnlyList<AttachmentInfo>? attachments,
        CancellationToken ct)
    {
        if (attachments == null || attachments.Count == 0)
        {
            return Task.FromResult(JobOfferAttachmentVerificationResult.Verified([]));
        }

        var expectedPrefix = $"{userId}/{jobOfferId}/";

        foreach (var attachment in attachments)
        {
            if (attachment.StoragePath.Contains("/missing/", StringComparison.Ordinal))
            {
                return Task.FromResult(
                    JobOfferAttachmentVerificationResult.Failed($"Attachment '{attachment.FileName}' was not found."));
            }

            if (!attachment.StoragePath.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    JobOfferAttachmentVerificationResult.Failed("Attachments must be uploaded into the current offer folder."));
            }

            var fileName = Path.GetFileName(attachment.StoragePath.Replace('\\', '/'));
            if (!string.Equals(fileName, attachment.FileName, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    JobOfferAttachmentVerificationResult.Failed("Attachment metadata does not match the uploaded file."));
            }
        }

        return Task.FromResult(JobOfferAttachmentVerificationResult.Verified(attachments));
    }
}
