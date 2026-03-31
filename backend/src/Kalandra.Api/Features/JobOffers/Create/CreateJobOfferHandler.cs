using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Create;

public class CreateJobOfferHandler
{
    private readonly IDocumentSession _session;

    public CreateJobOfferHandler(IDocumentSession session)
    {
        _session = session;
    }

    public async Task<CreateJobOfferResponse> HandleAsync(
        CreateJobOfferRequest request,
        string userId,
        string userEmail,
        CancellationToken ct)
    {
        var streamId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var submitted = new JobOfferSubmitted(
            UserId: userId,
            UserEmail: userEmail,
            CompanyName: request.CompanyName,
            ContactName: request.ContactName,
            ContactEmail: request.ContactEmail,
            JobTitle: request.JobTitle,
            Description: request.Description,
            SalaryRange: request.SalaryRange,
            Location: request.Location,
            IsRemote: request.IsRemote,
            AdditionalNotes: request.AdditionalNotes,
            Attachments: request.Attachments ?? [],
            Timestamp: now);

        _session.Events.StartStream<Entities.JobOffer>(streamId, submitted);
        await _session.SaveChangesAsync(ct);

        return new CreateJobOfferResponse(streamId, now);
    }
}
