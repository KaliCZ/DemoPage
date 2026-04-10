using Kalandra.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Supabase;

namespace Kalandra.Infrastructure.Avatars;

public class SupabaseAvatarService(
    Client supabase,
    SupabaseAuthConfig authConfig,
    IMemoryCache cache,
    ILogger<SupabaseAvatarService> logger) : IAvatarService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string AvatarBucket = "avatars";

    /// <summary>
    /// Resolves avatar URLs for the given user IDs. Returns only users that
    /// actually have an avatar — callers never need to filter nulls.
    /// </summary>
    public async Task<Dictionary<Guid, Uri>> GetAvatarUrlsAsync(
        IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, Uri>();

        foreach (var userId in userIds.Distinct())
        {
            // nulls are cached too.
            if (cache.TryGetValue(AvatarCacheKey(userId), out Uri? cached))
            {
                if (cached != null)
                    result[userId] = cached;
            }
            else
            {
                var avatarUrl = await FetchAvatarUrlAsync(userId);
                cache.Set(AvatarCacheKey(userId), avatarUrl, CacheDuration);
                if (avatarUrl != null)
                    result[userId] = avatarUrl;
            }
        }

        return result;
    }

    private async Task<Uri?> FetchAvatarUrlAsync(Guid userId)
    {
        try
        {
            var user = await supabase.AdminAuth(authConfig.ServiceKey.Value)
                .GetUserById(userId.ToString());

            if (user?.UserMetadata is Dictionary<string, object> metadata &&
                metadata.TryGetValue("avatar_url", out var avatarUrlObj) &&
                avatarUrlObj is string url &&
                !string.IsNullOrEmpty(url) &&
                Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch user {UserId} from Supabase Auth", userId);
        }

        return null;
    }

    public async Task<Uri> UploadAvatarAsync(
        Guid userId, Stream content, string contentType, CancellationToken ct)
    {
        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var storagePath = $"{userId}/avatar{extension}";

        // Delete any existing avatar files first
        await DeleteAvatarFilesAsync(userId, ct);

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);

        await supabase.Storage.From(AvatarBucket)
            .Upload(
                ms.ToArray(),
                storagePath,
                new Supabase.Storage.FileOptions { ContentType = contentType });

        var publicUrl = supabase.Storage.From(AvatarBucket).GetPublicUrl(storagePath);
        return new Uri($"{publicUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
    }

    public async Task UpdateAvatarUrlAsync(Guid userId, Uri? avatarUrl, CancellationToken ct)
    {
        await supabase.AdminAuth(authConfig.ServiceKey.Value)
            .UpdateUserById(
                userId.ToString(),
                new Supabase.Gotrue.AdminUserAttributes
                {
                    UserMetadata = new Dictionary<string, object>
                    {
                        ["avatar_url"] = avatarUrl?.ToString() ?? ""
                    }
                });

        InvalidateCache(userId);
    }

    public async Task DeleteAvatarFilesAsync(Guid userId, CancellationToken ct)
    {
        var storage = supabase.Storage.From(AvatarBucket);
        var files = await storage.List(userId.ToString());

        if (files is not { Count: > 0 })
            return;

        var paths = files.Select(f => $"{userId}/{f.Name}").ToList();
        await storage.Remove(paths);
    }

    private void InvalidateCache(Guid userId) => cache.Remove(AvatarCacheKey(userId));

    private static string AvatarCacheKey(Guid userId) => $"avatar:{userId}";
}
