using Kalandra.Api.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Kalandra.Api.Features.JobOffers.GetDetail;

public class GetJobOfferDetailHandler
{
    private readonly AppDbContext _db;

    public GetJobOfferDetailHandler(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Gets a job offer by ID. Returns null if not found.
    /// </summary>
    public async Task<GetJobOfferDetailResponse?> HandleAsync(
        Guid id,
        string? requesterUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        var offer = await _db.JobOffers
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, ct);

        if (offer == null)
            return null;

        // Non-admin users can only see their own offers
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
            offer.Status,
            isAdmin ? offer.AdminNotes : null,
            offer.UserEmail,
            offer.CreatedAt,
            offer.UpdatedAt);
    }
}
