using Kalandra.Blog;
using Kalandra.Blog.Stats;

namespace Kalandra.Api.Features.Blog;

/// <summary>
/// Keeps the blog-index stats snapshot fresh off the request path: every tick it recomputes each
/// post's totals so GET /api/blog/stats stays a by-id read instead of a live aggregate. Absolute
/// counts are recomputed each pass, so overlapping instances during a blue/green deploy converge
/// rather than conflict — no leader election needed.
/// </summary>
public class BlogStatsSnapshotService(
    IServiceScopeFactory scopeFactory,
    ILogger<BlogStatsSnapshotService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            await RefreshAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var catalog = scope.ServiceProvider.GetRequiredService<IBlogPostCatalog>();
            var refresher = scope.ServiceProvider.GetRequiredService<BlogStatsSnapshotRefresher>();
            foreach (var post in catalog.All)
                await refresher.RefreshAsync(post, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // A failed pass just serves a staler snapshot until the next tick — keep the loop alive.
            logger.LogError(ex, "Blog stats snapshot refresh failed");
        }
    }
}
