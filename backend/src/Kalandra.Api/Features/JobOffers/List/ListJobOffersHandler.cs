using Kalandra.Api.Features.JobOffers.Entities;
using Marten;

namespace Kalandra.Api.Features.JobOffers.List;

public class ListJobOffersHandler
{
    private readonly IQuerySession _session;

    public ListJobOffersHandler(IQuerySession session)
    {
        _session = session;
    }

    public async Task<ListJobOffersResponse> HandleAsync(
        string? userId,
        CancellationToken ct)
    {
        var query = _session.Query<JobOffer>();

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
