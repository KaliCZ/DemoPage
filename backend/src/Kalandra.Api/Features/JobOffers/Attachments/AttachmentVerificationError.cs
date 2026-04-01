namespace Kalandra.Api.Features.JobOffers.Attachments;

public enum AttachmentVerificationError
{
    ServiceUnavailable,
    PathTraversal,
    WrongFolder,
    MetadataMismatch,
    FileNotFound,
}
