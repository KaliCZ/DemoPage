namespace Kalandra.Infrastructure.Storage;

public interface IStorageService
{
    Task<IReadOnlyList<StorageFileInfo>> UploadAsync(
        string folderPrefix,
        IReadOnlyList<FileUploadItem> files,
        CancellationToken ct);

    Task<StorageDownloadResult> DownloadAsync(string storagePath, CancellationToken ct);

    /// <summary>
    /// Lightweight probe that the backing storage is reachable with the configured
    /// credentials. Throws if not. Used by the /health endpoint.
    /// </summary>
    Task PingAsync(CancellationToken ct);
}

public record FileUploadItem(
    string FileName,
    long FileSize,
    string ContentType,
    Stream Content);

public record StorageDownloadResult(Stream Content, long? ContentLength) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
    }
}
