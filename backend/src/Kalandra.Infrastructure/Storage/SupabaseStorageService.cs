using Microsoft.Extensions.Logging;
using Supabase.Storage;
using Supabase.Storage.Interfaces;

namespace Kalandra.Infrastructure.Storage;

public class SupabaseStorageService(
    Supabase.Storage.Client storage,
    ILogger<SupabaseStorageService> logger) : IStorageService
{
    public const string BucketName = "job-offer-attachments";

    public async Task<IReadOnlyList<StorageFileInfo>> UploadAsync(
        string folderPrefix,
        IReadOnlyList<FileUploadItem> files,
        CancellationToken ct)
    {
        var bucket = storage.From(BucketName);
        var uploaded = new List<StorageFileInfo>(files.Count);

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName);
            var storagePath = $"{folderPrefix}{Guid.NewGuid()}{extension}";

            try
            {
                using var ms = new MemoryStream();
                await file.Content.CopyToAsync(ms, ct);

                var fileOptions = new Supabase.Storage.FileOptions { ContentType = file.ContentType };
                await bucket.Upload(ms.ToArray(), storagePath, fileOptions, onProgress: null, inferContentType: true, ct);
            }
            catch
            {
                await CleanupAsync(bucket, uploaded);
                throw;
            }

            uploaded.Add(new StorageFileInfo(
                FileName: file.FileName,
                StoragePath: storagePath,
                FileSize: file.FileSize,
                ContentType: file.ContentType));
        }

        return uploaded;
    }

    public async Task<StorageDownloadResult> DownloadAsync(string storagePath, CancellationToken ct)
    {
        var bucket = storage.From(BucketName);
        var data = await bucket.Download(storagePath, (TransformOptions?)null);
        return new StorageDownloadResult(new MemoryStream(data), data.Length);
    }

    public async Task PingAsync(CancellationToken ct)
    {
        var searchOptions = new SearchOptions { Limit = 1 };
        await storage.From(BucketName).List(path: "", options: searchOptions);
    }


    private async Task CleanupAsync(IStorageFileApi<FileObject> bucket, List<StorageFileInfo> uploaded)
    {
        if (uploaded.Count == 0)
            return;

        try
        {
            var paths = uploaded.Select(f => f.StoragePath).ToList();
            await bucket.Remove(paths);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up {Count} uploaded files after upload failure", uploaded.Count);
        }
    }
}
