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
    private static readonly TimeSpan cacheDuration = TimeSpan.FromMinutes(5);
    private const string avatarBucket = "avatars";
    private readonly Supabase.Gotrue.Interfaces.IGotrueAdminClient<Supabase.Gotrue.User> adminAuthClient =
        supabase.AdminAuth(authConfig.ServiceKey.Value);

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
                cache.Set(AvatarCacheKey(userId), avatarUrl, cacheDuration);
                if (avatarUrl != null)
                    result[userId] = avatarUrl;
            }
        }

        return result;
    }

    public async Task<Uri> ReplaceAvatarAsync(
        Guid userId, Stream content, string contentType, CancellationToken ct)
    {
        var storagePath = $"{userId}/avatar";

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);

        var storage = supabase.Storage.From(avatarBucket);
        await storage.Upload(
            ms.ToArray(),
            storagePath,
            new Supabase.Storage.FileOptions { ContentType = contentType, Upsert = true });

        var publicUrl = storage.GetPublicUrl(storagePath);
        var avatarUri = new Uri($"{publicUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        await UpdateAvatarUrlAsync(userId, avatarUri);

        return avatarUri;
    }

    public async Task RemoveAvatarAsync(Guid userId, CancellationToken ct)
    {
        await supabase.Storage.From(avatarBucket).Remove([$"{userId}/avatar"]);
        await UpdateAvatarUrlAsync(userId, avatarUrl: null);
    }

    private async Task<Uri?> FetchAvatarUrlAsync(Guid userId)
    {
        try
        {
            var user = await adminAuthClient.GetUserById(userId.ToString());

            if (user?.UserMetadata is { } metadata &&
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

    private async Task UpdateAvatarUrlAsync(Guid userId, Uri? avatarUrl)
    {
        await adminAuthClient.UpdateUserById(userId.ToString(), new Supabase.Gotrue.AdminUserAttributes
        {
            UserMetadata = new Dictionary<string, object>
            {
                ["avatar_url"] = avatarUrl?.ToString() ?? ""
            }
        });

        cache.Remove(AvatarCacheKey(userId));
    }

    private static string AvatarCacheKey(Guid userId) => $"avatar:{userId}";
}
