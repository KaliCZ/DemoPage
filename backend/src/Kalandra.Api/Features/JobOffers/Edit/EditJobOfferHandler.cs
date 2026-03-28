using Kalandra.Api.Features.JobOffers.Create;
using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Edit;

public class EditJobOfferHandler
{
    private readonly IDocumentSession _session;

    public EditJobOfferHandler(IDocumentSession session)
    {
        _session = session;
    }

    public async Task<(bool Success, string? Error)> HandleAsync(
        Guid id,
        CreateJobOfferRequest request,
        string userId,
        string userEmail,
        CancellationToken ct)
    {
        var offer = await _session.Events.AggregateStreamAsync<JobOffer>(id, token: ct);
        if (offer == null)
            return (false, "Not found");

        if (offer.UserId != userId)
            return (false, "Not authorized");

        if (offer.Status != JobOfferStatus.Submitted)
            return (false, "Can only edit offers with status Submitted");

        var edited = new JobOfferEdited(
            EditedByUserId: userId,
            EditedByEmail: userEmail,
            CompanyName: request.CompanyName,
            ContactName: request.ContactName,
            ContactEmail: request.ContactEmail,
            JobTitle: request.JobTitle,
            Description: request.Description,
            SalaryRange: request.SalaryRange,
            Location: request.Location,
            IsRemote: request.IsRemote,
            AdditionalNotes: request.AdditionalNotes,
            Timestamp: DateTimeOffset.UtcNow);

        _session.Events.Append(id, edited);
        await _session.SaveChangesAsync(ct);
        return (true, null);
    }
}
