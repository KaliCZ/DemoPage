namespace Kalandra.Infrastructure.Storage;

public interface IStorageService
{
    Task<IReadOnlyList<StorageFileInfo>> UploadAsync(
        string folderPrefix,
        IReadOnlyList<FileUploadItem> files,
        CancellationToken ct);

    Task<StorageDownloadResult?> DownloadAsync(string storagePath, CancellationToken ct);

    string GetPublicUrl(string storagePath);
}

public record FileUploadItem(
    string FileName,
    long FileSize,
    string ContentType,
    Stream Content);

public record StorageDownloadResult(Stream Content, string ContentType, long? ContentLength) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
    }
}

public class StorageUploadException(string message) : Exception(message);
