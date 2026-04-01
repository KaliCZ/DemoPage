using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Infrastructure.Auth;
using Marten;
using Marten.Linq;
using Marten.Pagination;

namespace Kalandra.Api.Features.JobOffers.List;

public class ListJobOffersHandler(
    IQuerySession session,
    ICurrentUserAccessor currentUserAccessor)
{
    private const int MaxPageSize = 100;

    public async Task<ListJobOffersResponse> HandleAsync(
        JobOfferStatus? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);

        var user = currentUserAccessor.CurrentUser;
        var query = session.Query<JobOffer>();

        if (!user.IsAdmin)
        {
            query = (IMartenQueryable<JobOffer>)query.Where(j => j.UserId == user.Id);
        }

        if (status != null)
        {
            query = (IMartenQueryable<JobOffer>)query.Where(j => j.Status == status);
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
