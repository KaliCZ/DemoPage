using Kalandra.Api.Features.JobOffers.Attachments;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Create;

public class CreateJobOfferHandler(
    IDocumentSession session,
    IJobOfferAttachmentVerifier attachmentVerifier,
    TimeProvider timeProvider)
{
    public async Task<(bool Success, string? Error, CreateJobOfferResponse? Response)> HandleAsync(
        CreateJobOfferRequest request,
        string userId,
        string userEmail,
        CancellationToken ct)
    {
        var streamId = request.Id ?? Guid.NewGuid();
        var attachmentVerification = await attachmentVerifier.VerifyAsync(streamId, userId, request.Attachments, ct);
        if (!attachmentVerification.Success)
        {
            return (false, attachmentVerification.Error, null);
        }

        var now = timeProvider.GetUtcNow();

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
            Attachments: attachmentVerification.Attachments,
            Timestamp: now);

        session.Events.StartStream<Entities.JobOffer>(streamId, submitted);
        await session.SaveChangesAsync(ct);

        return (true, null, new CreateJobOfferResponse(streamId, now));
    }
}
