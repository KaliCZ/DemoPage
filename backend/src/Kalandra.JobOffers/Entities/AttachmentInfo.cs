namespace Kalandra.JobOffers.Entities;

public record AttachmentInfo(
    NonEmptyString FileName,
    NonEmptyString StoragePath,
    long FileSize,
    NonEmptyString ContentType);
