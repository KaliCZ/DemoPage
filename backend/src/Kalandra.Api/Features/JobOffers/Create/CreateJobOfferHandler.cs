using Kalandra.Api.Features.JobOffers.Attachments;
using Kalandra.Api.Features.JobOffers.Events;
using Marten;

namespace Kalandra.Api.Features.JobOffers.Create;

public class CreateJobOfferHandler(
    IDocumentSession session,
    IJobOfferAttachmentVerifier attachmentVerifier,
    TimeProvider timeProvider)
{
    public async Task<Try<CreateJobOfferResponse, CreateJobOfferError>> HandleAsync(
        CreateJobOfferRequest request,
        string userId,
        string userEmail,
        CancellationToken ct)
    {
        var streamId = request.Id ?? Guid.NewGuid();
        var attachmentResult = await attachmentVerifier.VerifyAsync(
            jobOfferId: streamId,
            userId: userId,
            attachments: request.Attachments,
            ct: ct);

        if (attachmentResult.IsError)
        {
            var attachmentError = attachmentResult.Error.Get((Unit _) => new InvalidOperationException());
            return Try.Error<CreateJobOfferResponse, CreateJobOfferError>(attachmentError switch
            {
                AttachmentVerificationError.ServiceUnavailable => CreateJobOfferError.AttachmentServiceUnavailable,
                AttachmentVerificationError.PathTraversal => CreateJobOfferError.AttachmentPathTraversal,
                AttachmentVerificationError.WrongFolder => CreateJobOfferError.AttachmentWrongFolder,
                AttachmentVerificationError.MetadataMismatch => CreateJobOfferError.AttachmentMetadataMismatch,
                AttachmentVerificationError.FileNotFound => CreateJobOfferError.AttachmentFileNotFound,
            });
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
            Attachments: attachmentResult.Success.Get((Unit _) => new InvalidOperationException()),
            Timestamp: now);

        session.Events.StartStream<Entities.JobOffer>(streamId, submitted);
        await session.SaveChangesAsync(ct);

        return Try.Success<CreateJobOfferResponse, CreateJobOfferError>(new CreateJobOfferResponse(streamId, now));
    }
}
