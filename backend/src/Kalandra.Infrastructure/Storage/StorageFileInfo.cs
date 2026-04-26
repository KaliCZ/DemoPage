using StrongTypes;

namespace Kalandra.Infrastructure.Storage;

public record StorageFileInfo(
    NonEmptyString FileName,
    NonEmptyString StoragePath,
    long FileSize,
    NonEmptyString ContentType);
