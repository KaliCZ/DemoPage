using Kalandra.Api.Features.JobOffers.Entities;
using Marten;

namespace Kalandra.Api.Features.JobOffers.GetDetail;

public class GetJobOfferDetailHandler(IQuerySession session)
{
    public async Task<GetJobOfferDetailResponse?> HandleAsync(
        Guid id,
        string? requesterUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(id, ct);
        if (offer == null)
            return null;

        if (!isAdmin && offer.UserId != requesterUserId)
            return null;

        return new GetJobOfferDetailResponse(
            Id: offer.Id,
            CompanyName: offer.CompanyName,
            ContactName: offer.ContactName,
            ContactEmail: offer.ContactEmail,
            JobTitle: offer.JobTitle,
            Description: offer.Description,
            SalaryRange: offer.SalaryRange,
            Location: offer.Location,
            IsRemote: offer.IsRemote,
            AdditionalNotes: offer.AdditionalNotes,
            Attachments: offer.Attachments,
            Status: offer.Status,
            AdminNotes: isAdmin ? offer.AdminNotes : null,
            UserEmail: offer.UserEmail,
            CreatedAt: offer.CreatedAt,
            UpdatedAt: offer.UpdatedAt);
    }
}
