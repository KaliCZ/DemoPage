using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Marten;
using Marten.Linq;
using Marten.Pagination;

namespace Kalandra.JobOffers.Queries;

public record ListJobOffersQuery(CurrentUser User, bool ShowAll, JobOfferStatus[]? Statuses, int Page, int PageSize);

public class ListJobOffersHandler(IQuerySession session)
{
    private const int MaxPageSize = 100;

    public async Task<IPagedList<JobOffer>> HandleAsync(ListJobOffersQuery query, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, MaxPageSize);

        var q = session.Query<JobOffer>();

        if (!query.ShowAll || !query.User.IsAdmin)
        {
            var userId = query.User.Id;
            q = (IMartenQueryable<JobOffer>)q.Where(j => j.UserId == userId);
        }

        if (query.Statuses is { Length: > 0 })
        {
            q = (IMartenQueryable<JobOffer>)q.Where(j => j.Status.In(query.Statuses));
        }

        return await q
            .OrderByDescending(j => j.CreatedAt)
            .ToPagedListAsync(query.Page, query.PageSize, ct);
    }
}
