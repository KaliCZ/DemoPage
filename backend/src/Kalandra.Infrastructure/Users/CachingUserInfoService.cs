using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Kalandra.Infrastructure.Users;

/// <summary>
/// Caches author profiles so a comment thread doesn't re-fetch each one from Supabase on every load.
/// Profile edits bypass this service, so freshness rests on the TTL plus an explicit <see cref="EvictAsync"/>.
/// </summary>
public class CachingUserInfoService(
    IUserInfoService source,
    IDistributedCache cache,
    ILogger<CachingUserInfoService> logger,
    TimeSpan? secondEvictionDelay = null) : IUserInfoService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly TimeSpan _secondEvictionDelay = secondEvictionDelay ?? TimeSpan.FromSeconds(5);

    private static string Key(Guid userId) => $"userinfo:{userId}";

    public async Task<Dictionary<Guid, UserPublicInfo>> GetUserInfoAsync(
        IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, UserPublicInfo>();
        var misses = new List<Guid>();

        foreach (var userId in userIds.Distinct())
        {
            if (await TryReadAsync(userId, ct) is { } cached)
                result[userId] = cached;
            else
                misses.Add(userId);
        }

        if (misses.Count > 0)
        {
            var fetched = await source.GetUserInfoAsync(misses, ct);
            foreach (var (userId, info) in fetched)
            {
                result[userId] = info;
                await WriteAsync(userId, info, ct);
            }
        }

        return result;
    }

    public async Task EvictAsync(Guid userId, CancellationToken ct)
    {
        await cache.RemoveAsync(Key(userId), ct);
        _ = EvictAgainAfterDelayAsync(userId);
    }

    // A read that fetched the old profile just before the update commits can write it back after
    // the immediate eviction above; this second pass clears that resurrected entry.
    private async Task EvictAgainAfterDelayAsync(Guid userId)
    {
        try
        {
            await Task.Delay(_secondEvictionDelay);
            await cache.RemoveAsync(Key(userId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Delayed user-info cache eviction failed for {UserId}", userId);
        }
    }

    public Task PingAsync(CancellationToken ct) => source.PingAsync(ct);

    private async Task<UserPublicInfo?> TryReadAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var bytes = await cache.GetAsync(Key(userId), ct);
            return bytes is null ? null : JsonSerializer.Deserialize<UserPublicInfo>(bytes);
        }
        catch (Exception ex)
        {
            // A cache hiccup must never fail the request — fall back to the source.
            logger.LogWarning(ex, "User-info cache read failed for {UserId}", userId);
            return null;
        }
    }

    private async Task WriteAsync(Guid userId, UserPublicInfo info, CancellationToken ct)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(info);
            var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl };
            await cache.SetAsync(Key(userId), bytes, options, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "User-info cache write failed for {UserId}", userId);
        }
    }
}
