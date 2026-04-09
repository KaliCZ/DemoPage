using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Marten;

namespace Kalandra.JobOffers.Queries;

public record GetAttachmentInfoQuery(Guid JobOfferId, string FileName, CurrentUser User);

public record AttachmentInfoResult(AttachmentInfo Attachment, string StoragePath);

public class GetAttachmentInfoHandler(IQuerySession session)
{
    public async Task<AttachmentInfoResult?> HandleAsync(
        GetAttachmentInfoQuery query, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(query.JobOfferId, ct);
        if (offer == null)
            return null;

        if (!query.User.IsAdmin && offer.UserId != query.User.Id)
            return null;

        var attachment = offer.Attachments.FirstOrDefault(
            a => string.Equals(a.FileName, query.FileName, StringComparison.Ordinal));
        if (attachment == null)
            return null;

        return new AttachmentInfoResult(attachment, attachment.StoragePath);
    }
}
