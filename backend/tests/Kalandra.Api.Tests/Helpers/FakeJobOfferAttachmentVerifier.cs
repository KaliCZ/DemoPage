using Kalandra.Api.Features.JobOffers.Attachments;
using Kalandra.Api.Features.JobOffers.Entities;

namespace Kalandra.Api.Tests.Helpers;

public class FakeJobOfferAttachmentVerifier : IJobOfferAttachmentVerifier
{
    public Task<Try<IReadOnlyList<AttachmentInfo>, AttachmentVerificationError>> VerifyAsync(
        Guid jobOfferId,
        string userId,
        IReadOnlyList<AttachmentInfo>? attachments,
        CancellationToken ct)
    {
        if (attachments == null || attachments.Count == 0)
        {
            return Task.FromResult(
                Try.Success<IReadOnlyList<AttachmentInfo>, AttachmentVerificationError>(
                    Array.Empty<AttachmentInfo>()));
        }

        var expectedPrefix = $"{userId}/{jobOfferId}/";

        foreach (var attachment in attachments)
        {
            if (attachment.StoragePath.Contains("/missing/", StringComparison.Ordinal))
            {
                return Task.FromResult(
                    Try.Error<IReadOnlyList<AttachmentInfo>, AttachmentVerificationError>(
                        AttachmentVerificationError.FileNotFound));
            }

            if (!attachment.StoragePath.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    Try.Error<IReadOnlyList<AttachmentInfo>, AttachmentVerificationError>(
                        AttachmentVerificationError.WrongFolder));
            }

            var fileName = Path.GetFileName(attachment.StoragePath.Replace('\\', '/'));
            if (!string.Equals(fileName, attachment.FileName, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    Try.Error<IReadOnlyList<AttachmentInfo>, AttachmentVerificationError>(
                        AttachmentVerificationError.MetadataMismatch));
            }
        }

        return Task.FromResult(
            Try.Success<IReadOnlyList<AttachmentInfo>, AttachmentVerificationError>(attachments));
    }
}
