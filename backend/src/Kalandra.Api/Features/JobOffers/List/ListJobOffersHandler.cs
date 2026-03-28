using Kalandra.Api.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Kalandra.Api.Features.JobOffers.List;

public class ListJobOffersHandler
{
    private readonly AppDbContext _db;

    public ListJobOffersHandler(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Lists job offers. If userId is null, returns all (admin). Otherwise filters by user.
    /// </summary>
    public async Task<ListJobOffersResponse> HandleAsync(
        string? userId,
        CancellationToken ct)
    {
        var query = _db.JobOffers.AsNoTracking();

        if (userId != null)
        {
            query = query.Where(j => j.UserId == userId);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobOfferSummary(
                j.Id,
                j.CompanyName,
                j.JobTitle,
                j.ContactEmail,
                j.Status,
                j.IsRemote,
                j.Location,
                j.CreatedAt))
            .ToListAsync(ct);

        return new ListJobOffersResponse(items, totalCount);
    }
}
