using System.Net;
using Kalandra.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace Kalandra.Infrastructure.Storage;

public class SupabaseStorageService(
    HttpClient httpClient,
    SupabaseAuthConfig authConfig,
    SupabaseStorageConfig storageConfig,
    ILogger<SupabaseStorageService> logger) : IStorageService
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

            using var content = new StreamContent(file.Content);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{projectUrl}/storage/v1/object/{EncodeBucketPath(bucketName, storagePath)}");
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

    public async Task<StorageDownloadResult?> DownloadAsync(string storagePath, CancellationToken ct)
    {
        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = storageConfig.ServiceKey.Value;
        var bucketName = storageConfig.BucketName.Value;

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{projectUrl}/storage/v1/object/{EncodeBucketPath(bucketName, storagePath)}");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);

        var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Failed to download {StoragePath} from Supabase Storage. Status: {StatusCode}",
                storagePath,
                (int)response.StatusCode);
            response.Dispose();
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var contentLength = response.Content.Headers.ContentLength;
        var stream = await response.Content.ReadAsStreamAsync(ct);

        return new StorageDownloadResult(stream, contentType, contentLength);
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
                using var deleteRequest = new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"{projectUrl}/storage/v1/object/{EncodeBucketPath(bucketName, file.StoragePath)}");
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

    private static string EncodeBucketPath(string bucketName, string storagePath)
    {
        var encodedBucket = Uri.EscapeDataString(bucketName);
        var encodedPath = string.Join(
            "/",
            storagePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return $"{encodedBucket}/{encodedPath}";
    }
}
