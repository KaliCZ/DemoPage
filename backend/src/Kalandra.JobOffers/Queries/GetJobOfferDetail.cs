using Kalandra.Infrastructure.Auth;
using Kalandra.JobOffers.Entities;
using Marten;

namespace Kalandra.JobOffers.Queries;

public record GetJobOfferDetailQuery(Guid Id, CurrentUser User);

public class GetJobOfferDetailHandler(IQuerySession session)
{
    public async Task<JobOffer?> HandleAsync(GetJobOfferDetailQuery query, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(query.Id, ct);
        if (offer == null)
            return null;

        if (!query.User.IsAdmin && offer.UserId != query.User.Id)
            return null;

        return offer;
    }
}
