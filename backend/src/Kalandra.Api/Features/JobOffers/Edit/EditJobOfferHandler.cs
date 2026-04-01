using Kalandra.Api.Features.JobOffers.Entities;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Edit;

public class EditJobOfferHandler(IDocumentSession session, TimeProvider timeProvider)
{
    public async Task<Try<Unit, EditJobOfferError>> HandleAsync(
        Guid id,
        EditJobOfferRequest request,
        string userId,
        string userEmail,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<JobOffer>(id, ct);
        var offer = stream.Aggregate;
        if (offer == null)
            return Try.Error<Unit, EditJobOfferError>(EditJobOfferError.NotFound);

        var result = offer.Edit(
            userId: userId,
            userEmail: userEmail,
            companyName: request.CompanyName,
            contactName: request.ContactName,
            contactEmail: request.ContactEmail,
            jobTitle: request.JobTitle,
            description: request.Description,
            salaryRange: request.SalaryRange,
            location: request.Location,
            isRemote: request.IsRemote,
            additionalNotes: request.AdditionalNotes,
            timestamp: timeProvider.GetUtcNow());

        if (result.IsError)
            return result.Map<Unit>(_ => Unit.Value);

        stream.AppendOne(result.Success.Get((Unit _) => new InvalidOperationException()));
        await session.SaveChangesAsync(ct);
        return Try.Success<Unit, EditJobOfferError>(Unit.Value);
    }
}
