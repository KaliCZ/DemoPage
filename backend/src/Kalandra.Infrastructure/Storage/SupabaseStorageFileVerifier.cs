using System.Net;
using Kalandra.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using StrongTypes;

namespace Kalandra.Infrastructure.Storage;

public class SupabaseStorageFileVerifier(
    HttpClient httpClient,
    SupabaseAuthConfig authConfig,
    SupabaseStorageConfig storageConfig,
    ILogger<SupabaseStorageFileVerifier> logger) : IStorageFileVerifier
{
    public async Task<Try<IReadOnlyList<StorageFileInfo>, FileVerificationError>> VerifyAsync(
        string expectedFolderPrefix,
        IReadOnlyList<StorageFileInfo>? files,
        CancellationToken ct)
    {
        if (files == null || files.Count == 0)
        {
            return Try.Success<IReadOnlyList<StorageFileInfo>, FileVerificationError>([]);
        }

        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = storageConfig.ServiceKey.Value;
        var bucketName = storageConfig.BucketName.Value;

        var verifiedFiles = new List<StorageFileInfo>(files.Count);

        foreach (var file in files)
        {
            var normalizedPath = NormalizePath(file.StoragePath);
            if (normalizedPath == null)
            {
                return Try.Error<IReadOnlyList<StorageFileInfo>, FileVerificationError>(
                    FileVerificationError.PathTraversal);
            }

            if (!normalizedPath.StartsWith(expectedFolderPrefix, StringComparison.Ordinal))
            {
                return Try.Error<IReadOnlyList<StorageFileInfo>, FileVerificationError>(
                    FileVerificationError.WrongFolder);
            }

            var fileName = Path.GetFileName(normalizedPath);
            if (!string.Equals(fileName, file.FileName, StringComparison.Ordinal))
            {
                return Try.Error<IReadOnlyList<StorageFileInfo>, FileVerificationError>(
                    FileVerificationError.MetadataMismatch);
            }

            var objectExists = await ObjectExistsAsync(projectUrl, bucketName, normalizedPath, serviceKey, ct);
            if (!objectExists)
            {
                return Try.Error<IReadOnlyList<StorageFileInfo>, FileVerificationError>(
                    FileVerificationError.FileNotFound);
            }

            verifiedFiles.Add(file with { StoragePath = normalizedPath });
        }

        return Try.Success<IReadOnlyList<StorageFileInfo>, FileVerificationError>(verifiedFiles);
    }

    private async Task<bool> ObjectExistsAsync(
        string projectUrl,
        string bucketName,
        string storagePath,
        string serviceKey,
        CancellationToken ct)
    {
        var encodedBucket = Uri.EscapeDataString(bucketName);
        var encodedPath = string.Join(
            "/",
            storagePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{projectUrl}/storage/v1/object/info/{encodedBucket}/{encodedPath}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Supabase storage verification failed for {StoragePath} with status code {StatusCode}.",
                storagePath,
                (int)response.StatusCode);

            return false;
        }

        return true;
    }

    private static string? NormalizePath(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return null;
        }

        var segments = storagePath
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            return null;
        }

        return string.Join('/', segments);
    }
}
