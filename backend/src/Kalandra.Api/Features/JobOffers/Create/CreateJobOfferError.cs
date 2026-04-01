namespace Kalandra.Api.Features.JobOffers.Create;

public enum CreateJobOfferError
{
    AttachmentServiceUnavailable,
    AttachmentPathTraversal,
    AttachmentWrongFolder,
    AttachmentMetadataMismatch,
    AttachmentFileNotFound,
}
