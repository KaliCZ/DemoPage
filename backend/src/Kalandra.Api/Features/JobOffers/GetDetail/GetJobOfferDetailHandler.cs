using Kalandra.Api.Features.JobOffers.Entities;
using Marten;

namespace Kalandra.Api.Features.JobOffers.GetDetail;

public class GetJobOfferDetailHandler
{
    private readonly IQuerySession _session;

    public GetJobOfferDetailHandler(IQuerySession session)
    {
        _session = session;
    }

    public async Task<GetJobOfferDetailResponse?> HandleAsync(
        Guid id,
        string? requesterUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        var offer = await _session.LoadAsync<JobOffer>(id, ct);
        if (offer == null)
            return null;

        if (!isAdmin && offer.UserId != requesterUserId)
            return null;

        return new GetJobOfferDetailResponse(
            offer.Id,
            offer.CompanyName,
            offer.ContactName,
            offer.ContactEmail,
            offer.JobTitle,
            offer.Description,
            offer.SalaryRange,
            offer.Location,
            offer.IsRemote,
            offer.AdditionalNotes,
            offer.Attachments,
            offer.Status,
            isAdmin ? offer.AdminNotes : null,
            offer.UserEmail,
            offer.CreatedAt,
            offer.UpdatedAt);
    }
}
