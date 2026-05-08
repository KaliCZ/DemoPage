using StrongTypes;

namespace Kalandra.JobOffers.Entities;

public record AttachmentInfo(
    NonEmptyString FileName,
    string StoragePath,
    long FileSize,
    NonEmptyString ContentType);
