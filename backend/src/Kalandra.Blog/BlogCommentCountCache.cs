using Microsoft.Extensions.Caching.Memory;

namespace Kalandra.Blog;

/// <summary>
/// Caches each post's live comment count so the blog index doesn't replay the whole comment
/// stream on every request. A write invalidates the entry, then invalidates it again after a
/// short delay in case a read already in flight repopulates it with the pre-write count.
/// </summary>
public sealed class BlogCommentCountCache(IMemoryCache cache, TimeProvider timeProvider) : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan RewipeDelay = TimeSpan.FromSeconds(20);
    private readonly CancellationTokenSource shutdown = new();

    public bool TryGet(Guid commentsStreamId, out int count) => cache.TryGetValue(Key(commentsStreamId), out count);

    public void Set(Guid commentsStreamId, int count) => cache.Set(Key(commentsStreamId), count, Lifetime);

    public void Invalidate(Guid commentsStreamId)
    {
        var key = Key(commentsStreamId);
        cache.Remove(key);
        _ = RewipeAfterDelay(key);
    }

    // Bound to the cache's lifetime so a pending wipe is cancelled at shutdown rather than firing on a disposed cache.
    private async Task RewipeAfterDelay(string key)
    {
        try
        {
            await Task.Delay(RewipeDelay, timeProvider, shutdown.Token);
            cache.Remove(key);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        shutdown.Cancel();
        shutdown.Dispose();
    }

    private static string Key(Guid commentsStreamId) => $"blog:comment-count:{commentsStreamId}";
}
