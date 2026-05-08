namespace Kalandra.Infrastructure.Storage;

public record StorageFileInfo(
    string FileName,
    string StoragePath,
    long FileSize,
    string ContentType);
