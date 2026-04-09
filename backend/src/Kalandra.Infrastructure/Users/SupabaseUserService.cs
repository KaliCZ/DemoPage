using System.Net.Http.Json;
using System.Text.Json;
using Kalandra.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Kalandra.Infrastructure.Users;

public class SupabaseUserService(
    HttpClient httpClient,
    SupabaseAuthConfig authConfig,
    IMemoryCache cache,
    ILogger<SupabaseUserService> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Resolves avatar URLs for the given user IDs. Returns only users that
    /// actually have an avatar — callers never need to filter nulls.
    /// </summary>
    public async Task<Dictionary<Guid, Uri>> GetAvatarUrlsAsync(
        IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, Uri>();
        var uncached = new List<Guid>();

        foreach (var userId in userIds.Distinct())
        {
            if (cache.TryGetValue(AvatarCacheKey(userId), out Uri? cached))
            {
                if (cached != null)
                    result[userId] = cached;
            }
            else
            {
                uncached.Add(userId);
            }
        }

        foreach (var userId in uncached)
        {
            var avatarUrl = await FetchAvatarUrlAsync(userId, ct);
            cache.Set(AvatarCacheKey(userId), avatarUrl, CacheDuration);
            if (avatarUrl != null)
                result[userId] = avatarUrl;
        }

        return result;
    }

    public void InvalidateCache(Guid userId) =>
        cache.Remove(AvatarCacheKey(userId));

    private async Task<Uri?> FetchAvatarUrlAsync(Guid userId, CancellationToken ct)
    {
        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = authConfig.ServiceKey.Value;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{projectUrl}/auth/v1/admin/users/{Uri.EscapeDataString(userId.ToString())}");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);

        using var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Failed to fetch user {UserId} from Supabase Auth. Status: {StatusCode}",
                userId,
                (int)response.StatusCode);
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        if (doc.RootElement.TryGetProperty("user_metadata", out var metadata) &&
            metadata.TryGetProperty("avatar_url", out var avatarUrl))
        {
            var url = avatarUrl.GetString();
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri;
        }

        return null;
    }

    public async Task<Uri> UploadAvatarAsync(
        Guid userId, Stream content, string contentType, CancellationToken ct)
    {
        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = authConfig.ServiceKey.Value;
        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var storagePath = $"{userId}/avatar{extension}";

        // Delete any existing avatar files first
        await DeleteAvatarFilesAsync(userId, ct);

        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{projectUrl}/storage/v1/object/avatars/{Uri.EscapeDataString(userId.ToString())}/avatar{extension}");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);
        request.Content = streamContent;

        using var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Failed to upload avatar for user {UserId}. Status: {StatusCode}. Response: {Body}",
                userId,
                (int)response.StatusCode,
                body);
            throw new InvalidOperationException($"Failed to upload avatar. Status: {(int)response.StatusCode}");
        }

        // Return public URL
        return new Uri($"{projectUrl}/storage/v1/object/public/avatars/{Uri.EscapeDataString(userId.ToString())}/avatar{extension}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
    }

    public async Task UpdateAvatarUrlAsync(Guid userId, Uri? avatarUrl, CancellationToken ct)
    {
        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = authConfig.ServiceKey.Value;

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"{projectUrl}/auth/v1/admin/users/{Uri.EscapeDataString(userId.ToString())}");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);
        request.Content = JsonContent.Create(new
        {
            user_metadata = new { avatar_url = avatarUrl?.ToString() ?? "" }
        });

        using var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Failed to update avatar_url for user {UserId}. Status: {StatusCode}. Response: {Body}",
                userId,
                (int)response.StatusCode,
                body);
            throw new InvalidOperationException($"Failed to update user avatar. Status: {(int)response.StatusCode}");
        }

        InvalidateCache(userId);
    }

    public async Task DeleteAvatarFilesAsync(Guid userId, CancellationToken ct)
    {
        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = authConfig.ServiceKey.Value;

        // List files in user's avatar folder
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{projectUrl}/storage/v1/object/list/avatars");
        listRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceKey);
        listRequest.Headers.Add("apikey", serviceKey);
        listRequest.Content = JsonContent.Create(new { prefix = $"{userId}/" });

        using var listResponse = await httpClient.SendAsync(listRequest, ct);
        if (!listResponse.IsSuccessStatusCode)
            return;

        using var doc = await JsonDocument.ParseAsync(
            await listResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        var filePaths = new List<string>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("name", out var name))
                filePaths.Add($"{userId}/{name.GetString()}");
        }

        if (filePaths.Count == 0)
            return;

        // Delete files
        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{projectUrl}/storage/v1/object/avatars");
        deleteRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceKey);
        deleteRequest.Headers.Add("apikey", serviceKey);
        deleteRequest.Content = JsonContent.Create(new { prefixes = filePaths });

        using var deleteResponse = await httpClient.SendAsync(deleteRequest, ct);

        if (!deleteResponse.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Failed to delete avatar files for user {UserId}. Status: {StatusCode}",
                userId,
                (int)deleteResponse.StatusCode);
        }
    }

    private static string AvatarCacheKey(Guid userId) => $"avatar:{userId}";
}
