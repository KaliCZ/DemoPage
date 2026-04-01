using Kalandra.Api.Features.JobOffers.Entities;
using Marten;
using Marten.Linq;
using Marten.Pagination;

namespace Kalandra.Api.Features.JobOffers.List;

public class ListJobOffersHandler(IQuerySession session)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<ListJobOffersResponse> HandleAsync(
        string? userId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = session.Query<JobOffer>();

        if (userId != null)
        {
            query = (IMartenQueryable<JobOffer>)query.Where(j => j.UserId == userId);
        }

        var pagedResult = await query
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
            .ToPagedListAsync(page, pageSize, ct);

        return new ListJobOffersResponse(pagedResult.ToList(), (int)pagedResult.TotalItemCount);
    }
}
