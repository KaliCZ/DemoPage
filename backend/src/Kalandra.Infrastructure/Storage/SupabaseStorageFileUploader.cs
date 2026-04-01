using System.Net;
using Kalandra.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace Kalandra.Infrastructure.Storage;

public class SupabaseStorageFileUploader(
    HttpClient httpClient,
    SupabaseAuthConfig authConfig,
    SupabaseStorageConfig storageConfig,
    ILogger<SupabaseStorageFileUploader> logger) : IStorageFileUploader
{
    public async Task<IReadOnlyList<StorageFileInfo>> UploadAsync(
        string folderPrefix,
        IReadOnlyList<FileUploadItem> files,
        CancellationToken ct)
    {
        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = storageConfig.ServiceKey.Value;
        var bucketName = storageConfig.BucketName.Value;

        var uploaded = new List<StorageFileInfo>(files.Count);

        foreach (var file in files)
        {
            var storagePath = $"{folderPrefix}{file.FileName}";
            var encodedBucket = Uri.EscapeDataString(bucketName);
            var encodedPath = string.Join(
                "/",
                storagePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));

            using var content = new StreamContent(file.Content);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{projectUrl}/storage/v1/object/{encodedBucket}/{encodedPath}");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceKey);
            request.Headers.Add("apikey", serviceKey);
            request.Content = content;

            using var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Failed to upload {StoragePath} to Supabase Storage. Status: {StatusCode}",
                    storagePath,
                    (int)response.StatusCode);

                // Best-effort cleanup of already-uploaded files
                await CleanupAsync(projectUrl, bucketName, serviceKey, uploaded, ct);

                throw new StorageUploadException(
                    $"Failed to upload file '{file.FileName}' to storage.");
            }

            uploaded.Add(new StorageFileInfo(
                FileName: file.FileName,
                StoragePath: storagePath,
                FileSize: file.FileSize,
                ContentType: file.ContentType));
        }

        return uploaded;
    }

    private async Task CleanupAsync(
        string projectUrl,
        string bucketName,
        string serviceKey,
        List<StorageFileInfo> uploaded,
        CancellationToken ct)
    {
        foreach (var file in uploaded)
        {
            try
            {
                var encodedBucket = Uri.EscapeDataString(bucketName);
                var encodedPath = string.Join(
                    "/",
                    file.StoragePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Uri.EscapeDataString));

                using var deleteRequest = new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"{projectUrl}/storage/v1/object/{encodedBucket}/{encodedPath}");
                deleteRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceKey);
                deleteRequest.Headers.Add("apikey", serviceKey);

                await httpClient.SendAsync(deleteRequest, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to clean up uploaded file {StoragePath} after upload failure.",
                    file.StoragePath);
            }
        }
    }
}
