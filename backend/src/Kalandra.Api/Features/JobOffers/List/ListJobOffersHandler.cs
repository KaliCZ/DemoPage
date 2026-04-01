using Kalandra.Api.Features.JobOffers.Entities;
using Marten;
using Marten.Linq;
using Marten.Pagination;

namespace Kalandra.Api.Features.JobOffers.List;

public class ListJobOffersHandler(IQuerySession session)
{
    private const int MaxPageSize = 100;

    public async Task<ListJobOffersResponse> HandleAsync(
        string? userId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);

        var query = session.Query<JobOffer>();

        if (userId != null)
        {
            query = (IMartenQueryable<JobOffer>)query.Where(j => j.UserId == userId);
        }

        var pagedResult = await query
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobOfferSummary(
                Id: j.Id,
                CompanyName: j.CompanyName,
                JobTitle: j.JobTitle,
                ContactEmail: j.ContactEmail,
                Status: j.Status,
                IsRemote: j.IsRemote,
                Location: j.Location,
                CreatedAt: j.CreatedAt))
            .ToPagedListAsync(page, pageSize, ct);

        return new ListJobOffersResponse(pagedResult.ToList(), (int)pagedResult.TotalItemCount);
    }
}
