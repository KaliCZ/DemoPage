using Microsoft.Extensions.Logging;
using Supabase.Storage;
using Supabase.Storage.Interfaces;

namespace Kalandra.Infrastructure.Storage;

public class SupabaseStorageService(
    Supabase.Client supabase,
    ILogger<SupabaseStorageService> logger) : IStorageService
{
    private const string BucketName = "job-offer-attachments";

    public async Task<IReadOnlyList<StorageFileInfo>> UploadAsync(
        string folderPrefix,
        IReadOnlyList<FileUploadItem> files,
        CancellationToken ct)
    {
        var bucket = supabase.Storage.From(BucketName);
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
                await bucket.Upload(ms.ToArray(), storagePath, fileOptions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload {StoragePath} to Supabase Storage", storagePath);
                await CleanupAsync(bucket, uploaded);
                throw new StorageUploadException($"Failed to upload file '{file.FileName}' to storage.");
            }

            uploaded.Add(new StorageFileInfo(
                FileName: file.FileName,
                StoragePath: storagePath,
                FileSize: file.FileSize,
                ContentType: file.ContentType));
        }

        return uploaded;
    }

    public async Task<StorageDownloadResult?> DownloadAsync(string storagePath, CancellationToken ct)
    {
        var bucket = supabase.Storage.From(BucketName);

        try
        {
            var data = await bucket.Download(storagePath, (TransformOptions?)null);
            var contentType = InferContentType(storagePath);
            var stream = new MemoryStream(data);

            return new StorageDownloadResult(stream, contentType, data.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download {StoragePath} from Supabase Storage", storagePath);
            return null;
        }
    }

    public string GetPublicUrl(string storagePath)
    {
        return supabase.Storage.From(BucketName).GetPublicUrl(storagePath, transformOptions: null);
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

    private static string InferContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }
}
