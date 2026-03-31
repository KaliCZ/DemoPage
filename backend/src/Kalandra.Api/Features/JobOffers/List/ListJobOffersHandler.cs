using Kalandra.Api.Features.JobOffers.Entities;
using Marten;
using Marten.Linq;

namespace Kalandra.Api.Features.JobOffers.List;

public class ListJobOffersHandler(IQuerySession session)
{
    public async Task<ListJobOffersResponse> HandleAsync(
        string? userId,
        CancellationToken ct)
    {
        var query = session.Query<JobOffer>();

        if (userId != null)
        {
            query = (IMartenQueryable<JobOffer>)query.Where(j => j.UserId == userId);
        }

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

        return new ListJobOffersResponse(items, items.Count);
    }
}
