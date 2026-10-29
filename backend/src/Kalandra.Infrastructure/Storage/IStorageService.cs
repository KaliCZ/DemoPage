using StrongTypes;

namespace Kalandra.Infrastructure.Storage;

public interface IStorageService
{
    Task<IReadOnlyList<StorageFileInfo>> UploadAsync(
        string folderPrefix,
        IReadOnlyList<FileUploadItem> files,
        CancellationToken ct);

    Task<StorageDownloadResult> DownloadAsync(string storagePath, CancellationToken ct);

    /// <summary>Throws if the storage is unreachable or rejects the configured credentials.</summary>
    Task PingAsync(CancellationToken ct);
}

public record FileUploadItem(
    NonEmptyString FileName,
    long FileSize,
    NonEmptyString ContentType,
    Stream Content);

public record StorageDownloadResult(Stream Content, long? ContentLength) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
    }
}
