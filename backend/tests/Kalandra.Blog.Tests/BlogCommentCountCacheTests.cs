using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;

namespace Kalandra.Blog.Tests;

public class BlogCommentCountCacheTests
{
    private static readonly Guid StreamId = new("b1090001-0000-4000-8000-0000000000c0");

    private static (BlogCommentCountCache cache, FakeTimeProvider time) NewCache()
    {
        var time = new FakeTimeProvider();
        return (new BlogCommentCountCache(new MemoryCache(new MemoryCacheOptions()), time), time);
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsTheCachedCount()
    {
        var (cache, _) = NewCache();

        cache.Set(StreamId, 7);

        Assert.True(cache.TryGet(StreamId, out var count));
        Assert.Equal(7, count);
    }

    [Fact]
    public void TryGet_WhenNothingCached_ReturnsFalse()
    {
        var (cache, _) = NewCache();

        Assert.False(cache.TryGet(StreamId, out _));
    }

    [Fact]
    public void Invalidate_RemovesTheEntryImmediately()
    {
        var (cache, _) = NewCache();
        cache.Set(StreamId, 3);

        cache.Invalidate(StreamId);

        Assert.False(cache.TryGet(StreamId, out _));
    }

    [Fact]
    public async Task Invalidate_WipesAgainAfterTheDelay_ClearingARacingRepopulation()
    {
        var (cache, time) = NewCache();
        cache.Invalidate(StreamId);

        // A read that began before the write lands its pre-write count just after the immediate wipe.
        cache.Set(StreamId, 3);
        time.Advance(TimeSpan.FromSeconds(20));

        await AssertEventually(() => !cache.TryGet(StreamId, out _), "cached count was not wiped after the delay");
    }

    [Fact]
    public void Dispose_CancelsAPendingRewipe_SoItNeverFiresAfterShutdown()
    {
        var (cache, time) = NewCache();
        cache.Invalidate(StreamId);
        cache.Set(StreamId, 3);

        cache.Dispose();
        time.Advance(TimeSpan.FromSeconds(20));

        // Dispose cancels the pending rewipe synchronously, so the entry survives untouched.
        Assert.True(cache.TryGet(StreamId, out var count));
        Assert.Equal(3, count);
    }

    private static async Task AssertEventually(Func<bool> condition, string because)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(5);
        }
        Assert.True(condition(), because);
    }
}
