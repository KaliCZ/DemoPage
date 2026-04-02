using Kalandra.JobOffers.Entities;
using Marten;

namespace Kalandra.JobOffers.Queries;

public record GetJobOfferDetailQuery(Guid Id, string UserId, bool IsAdmin);

public class GetJobOfferDetailHandler(IQuerySession session)
{
    public async Task<JobOffer?> HandleAsync(GetJobOfferDetailQuery query, CancellationToken ct)
    {
        var offer = await session.LoadAsync<JobOffer>(query.Id, ct);
        if (offer == null)
            return null;

        if (!query.IsAdmin && offer.UserId != query.UserId)
            return null;

        return offer;
    }
}
