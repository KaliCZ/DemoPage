namespace Kalandra.Api.Features.JobOffers.Entities;

public record AttachmentInfo(
    string FileName,
    string StoragePath,
    long FileSize,
    string ContentType);
