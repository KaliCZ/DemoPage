using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Edit;

public class EditJobOfferHandler(IDocumentSession session, TimeProvider timeProvider)
{
    public async Task<(bool Success, string? Error)> HandleAsync(
        Guid id,
        EditJobOfferRequest request,
        string userId,
        string userEmail,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(id, ct);
        var offer = stream.Aggregate;
        if (offer == null)
            return (false, "Not found");

        var (success, error, edited) = offer.Edit(
            userId,
            userEmail,
            request.CompanyName,
            request.ContactName,
            request.ContactEmail,
            request.JobTitle,
            request.Description,
            request.SalaryRange,
            request.Location,
            request.IsRemote,
            request.AdditionalNotes,
            timeProvider.GetUtcNow());

        if (!success || edited == null)
            return (false, error);

        stream.AppendOne(edited);
        await session.SaveChangesAsync(ct);
        return (true, null);
    }
}
