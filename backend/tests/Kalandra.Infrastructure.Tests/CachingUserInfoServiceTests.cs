using Kalandra.Infrastructure.Users;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kalandra.Infrastructure.Tests;

public class CachingUserInfoServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Stand-in source that counts fetches, so a test can assert a read was served from cache.</summary>
    private sealed class CountingUserInfoService : IUserInfoService
    {
        public int Calls { get; private set; }
        public Dictionary<Guid, UserPublicInfo> Profiles { get; } = new();

        public Task<Dictionary<Guid, UserPublicInfo>> GetUserInfoAsync(IEnumerable<Guid> userIds, CancellationToken ct)
        {
            Calls++;
            var result = userIds.Distinct().Where(Profiles.ContainsKey).ToDictionary(id => id, id => Profiles[id]);
            return Task.FromResult(result);
        }

        public Task EvictAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;
        public Task PingAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static CachingUserInfoService Build(out CountingUserInfoService source)
    {
        source = new CountingUserInfoService();
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return new CachingUserInfoService(source, cache, NullLogger<CachingUserInfoService>.Instance);
    }

    [Fact]
    public async Task SecondReadOfTheSameUser_IsServedFromCache()
    {
        var service = Build(out var source);
        var userId = Guid.NewGuid();
        source.Profiles[userId] = new UserPublicInfo("Ada", new Uri("https://cdn.test/ada.png"));

        await service.GetUserInfoAsync([userId], Ct);
        var second = await service.GetUserInfoAsync([userId], Ct);

        Assert.Equal("Ada", second[userId].DisplayName);
        Assert.Equal(new Uri("https://cdn.test/ada.png"), second[userId].AvatarUrl);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Evict_ForcesAFreshSourceRead()
    {
        var service = Build(out var source);
        var userId = Guid.NewGuid();
        source.Profiles[userId] = new UserPublicInfo("Old Name", null);

        await service.GetUserInfoAsync([userId], Ct);
        source.Profiles[userId] = new UserPublicInfo("New Name", null);
        await service.EvictAsync(userId, Ct);
        var afterEvict = await service.GetUserInfoAsync([userId], Ct);

        Assert.Equal("New Name", afterEvict[userId].DisplayName);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Evict_AlsoClearsAnEntryReCachedByARacingRead()
    {
        var source = new CountingUserInfoService();
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new CachingUserInfoService(source, cache, NullLogger<CachingUserInfoService>.Instance,
            secondEvictionDelay: TimeSpan.FromMilliseconds(50));
        var userId = Guid.NewGuid();
        source.Profiles[userId] = new UserPublicInfo("Old", null);

        await service.GetUserInfoAsync([userId], Ct);
        await service.EvictAsync(userId, Ct);
        // The racing read: re-caches the (stale) profile right after the immediate eviction.
        await service.GetUserInfoAsync([userId], Ct);

        // The delayed second eviction clears the resurrected entry.
        for (var attempt = 0; ; attempt++)
        {
            if (await cache.GetAsync($"userinfo:{userId}", Ct) is null) break;
            Assert.True(attempt < 100, "the delayed second eviction never cleared the entry");
            await Task.Delay(25, Ct);
        }
    }

    [Fact]
    public async Task MixedHitAndMiss_FetchesOnlyTheMiss()
    {
        var service = Build(out var source);
        var cached = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        source.Profiles[cached] = new UserPublicInfo("Cached", null);
        source.Profiles[fresh] = new UserPublicInfo("Fresh", null);

        await service.GetUserInfoAsync([cached], Ct);
        var combined = await service.GetUserInfoAsync([cached, fresh], Ct);

        Assert.Equal("Cached", combined[cached].DisplayName);
        Assert.Equal("Fresh", combined[fresh].DisplayName);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task UnresolvedProfile_IsNotRefetchedWhileTheNegativeEntryLives()
    {
        var service = Build(out var source);
        var unknown = Guid.NewGuid();

        var first = await service.GetUserInfoAsync([unknown], Ct);
        var second = await service.GetUserInfoAsync([unknown], Ct);

        Assert.Empty(first);
        Assert.Empty(second);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task UnresolvedProfile_IsRefetchedAfterTheNegativeEntryExpires()
    {
        var source = new CountingUserInfoService();
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new CachingUserInfoService(source, cache, NullLogger<CachingUserInfoService>.Instance,
            negativeTtl: TimeSpan.FromMilliseconds(50));
        var userId = Guid.NewGuid();

        await service.GetUserInfoAsync([userId], Ct);
        source.Profiles[userId] = new UserPublicInfo("Late Arrival", null);
        await Task.Delay(200, Ct);
        var afterExpiry = await service.GetUserInfoAsync([userId], Ct);

        Assert.Equal("Late Arrival", afterExpiry[userId].DisplayName);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Evict_AlsoClearsANegativeEntry()
    {
        var service = Build(out var source);
        var userId = Guid.NewGuid();

        await service.GetUserInfoAsync([userId], Ct);
        source.Profiles[userId] = new UserPublicInfo("Now Exists", null);
        await service.EvictAsync(userId, Ct);
        var afterEvict = await service.GetUserInfoAsync([userId], Ct);

        Assert.Equal("Now Exists", afterEvict[userId].DisplayName);
        Assert.Equal(2, source.Calls);
    }
}
