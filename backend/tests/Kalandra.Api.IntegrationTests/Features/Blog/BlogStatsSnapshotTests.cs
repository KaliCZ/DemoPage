using Kalandra.Api.IntegrationTests.Helpers;
using Kalandra.Blog.Stats;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.Api.IntegrationTests.Features.Blog;

public class BlogStatsSnapshotTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private BlogStatsSnapshotStore Store => factory.Services.GetRequiredService<BlogStatsSnapshotStore>();

    [Fact]
    public async Task PerColumnWrites_DoNotOverwriteEachOther()
    {
        var slug = $"snapshot-{Guid.NewGuid():N}";
        var store = Store;
        await store.EnsureTableAsync(Ct);

        // Two independent metric writers touch the same row through different columns. A single-document
        // (JSON) snapshot would let the second write clobber the first; separate columns must not.
        await store.SetTotalViewsAsync(slug, 7, Ct);
        await store.SetTotalCommentsAsync(slug, 3, Ct);

        var snapshot = (await store.LoadAsync([slug], Ct))[slug];
        Assert.Equal(7, snapshot.TotalViews);
        Assert.Equal(3, snapshot.TotalComments);
        // The columns the two writers never touched keep their defaults.
        Assert.Equal(0, snapshot.UniqueVisitors);
        Assert.Equal(0, snapshot.TotalReactions);
    }

    [Fact]
    public async Task WritingOneMetricRepeatedly_LeavesOtherMetricsIntact()
    {
        var slug = $"snapshot-{Guid.NewGuid():N}";
        var store = Store;
        await store.EnsureTableAsync(Ct);

        await store.SetTotalReactionsAsync(slug, 5, Ct);
        // A later refresh of only the views column must not reset reactions.
        await store.SetTotalViewsAsync(slug, 2, Ct);
        await store.SetTotalViewsAsync(slug, 9, Ct);

        var snapshot = (await store.LoadAsync([slug], Ct))[slug];
        Assert.Equal(9, snapshot.TotalViews);
        Assert.Equal(5, snapshot.TotalReactions);
    }

    [Fact]
    public async Task LoadAsync_UnknownSlug_IsAbsent()
    {
        var store = Store;
        await store.EnsureTableAsync(Ct);

        var result = await store.LoadAsync([$"missing-{Guid.NewGuid():N}"], Ct);

        Assert.Empty(result);
    }
}
