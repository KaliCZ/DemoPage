using System.Net;
using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace Kalandra.Api.Features.JobOffers.Attachments;

public class SupabaseJobOfferAttachmentVerifier(
    HttpClient httpClient,
    IOptions<SupabaseJwtOptions> authOptions,
    IOptions<SupabaseStorageOptions> storageOptions,
    ILogger<SupabaseJobOfferAttachmentVerifier> logger) : IJobOfferAttachmentVerifier
{
    public async Task<JobOfferAttachmentVerificationResult> VerifyAsync(
        Guid jobOfferId,
        string userId,
        IReadOnlyList<AttachmentInfo>? attachments,
        CancellationToken ct)
    {
        if (attachments == null || attachments.Count == 0)
        {
            return JobOfferAttachmentVerificationResult.Verified([]);
        }

        var projectUrl = authOptions.Value.SupabaseProjectUrl?.TrimEnd('/');
        var serviceKey = storageOptions.Value.ServiceKey?.Trim();
        var bucketName = storageOptions.Value.BucketName?.Trim();

        if (string.IsNullOrWhiteSpace(projectUrl) ||
            string.IsNullOrWhiteSpace(serviceKey) ||
            string.IsNullOrWhiteSpace(bucketName))
        {
            logger.LogError(
                "Attachment verification is misconfigured. Supabase project URL, storage bucket, and service key are required.");

            return JobOfferAttachmentVerificationResult.Failed("Attachments are temporarily unavailable.");
        }

        var verifiedAttachments = new List<AttachmentInfo>(attachments.Count);

        foreach (var attachment in attachments)
        {
            var normalizedPath = NormalizePath(attachment.StoragePath);
            if (normalizedPath == null)
            {
                return JobOfferAttachmentVerificationResult.Failed("Attachment paths must stay within the user's offer folder.");
            }

            var expectedPrefix = $"{userId}/{jobOfferId}/";
            if (!normalizedPath.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                return JobOfferAttachmentVerificationResult.Failed("Attachments must be uploaded into the current offer folder.");
            }

            var fileName = Path.GetFileName(normalizedPath);
            if (!string.Equals(fileName, attachment.FileName, StringComparison.Ordinal))
            {
                return JobOfferAttachmentVerificationResult.Failed("Attachment metadata does not match the uploaded file.");
            }

            var objectExists = await ObjectExistsAsync(projectUrl, bucketName, normalizedPath, serviceKey, ct);
            if (!objectExists)
            {
                return JobOfferAttachmentVerificationResult.Failed($"Attachment '{attachment.FileName}' was not found.");
            }

            verifiedAttachments.Add(attachment with { StoragePath = normalizedPath });
        }

        return JobOfferAttachmentVerificationResult.Verified(verifiedAttachments);
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
