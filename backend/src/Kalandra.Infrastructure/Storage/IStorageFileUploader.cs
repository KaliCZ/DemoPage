namespace Kalandra.Infrastructure.Storage;

public interface IStorageFileUploader
{
    Task<IReadOnlyList<StorageFileInfo>> UploadAsync(
        string folderPrefix,
        IReadOnlyList<FileUploadItem> files,
        CancellationToken ct);
}

public record FileUploadItem(
    string FileName,
    long FileSize,
    string ContentType,
    Stream Content);
