using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Marten;
using Marten.Linq;
using Marten.Pagination;

namespace Kalandra.JobOffers.Queries;

public record ListJobOffersQuery(CurrentUser User, bool ShowAll, JobOfferStatus[]? Statuses, int Page, int PageSize);

public record ListJobOffersResult(
    IReadOnlyList<JobOffer> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int PageCount,
    bool HasNextPage,
    bool HasPreviousPage);

public class ListJobOffersHandler(IQuerySession session)
{
    private const int MaxPageSize = 100;

    public async Task<ListJobOffersResult> HandleAsync(ListJobOffersQuery query, CancellationToken ct)
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

        var pagedResult = await q
            .OrderByDescending(j => j.CreatedAt)
            .ToPagedListAsync(query.Page, query.PageSize, ct);

        return new ListJobOffersResult(
            Items: pagedResult.ToList(),
            TotalCount: (int)pagedResult.TotalItemCount,
            Page: (int)pagedResult.PageNumber,
            PageSize: (int)pagedResult.PageSize,
            PageCount: (int)pagedResult.PageCount,
            HasNextPage: pagedResult.HasNextPage,
            HasPreviousPage: pagedResult.HasPreviousPage);
    }
}
