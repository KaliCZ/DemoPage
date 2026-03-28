using Kalandra.Api.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Kalandra.Api.Features.JobOffers.UpdateStatus;

public class UpdateJobOfferStatusHandler
{
    private readonly AppDbContext _db;

    public UpdateJobOfferStatusHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HandleAsync(
        Guid id,
        UpdateJobOfferStatusRequest request,
        CancellationToken ct)
    {
        var offer = await _db.JobOffers.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (offer == null)
            return false;

        offer.Status = request.Status;
        offer.AdminNotes = request.AdminNotes ?? offer.AdminNotes;
        offer.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
