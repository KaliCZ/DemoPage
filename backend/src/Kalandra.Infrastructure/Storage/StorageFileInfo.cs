using StrongTypes;

namespace Kalandra.Infrastructure.Storage;

public record StorageFileInfo(
    NonEmptyString FileName,
    string StoragePath,
    long FileSize,
    NonEmptyString ContentType);
