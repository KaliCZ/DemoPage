using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Kalandra.Hosting.Tests;

public class MartenDaemonPollSamplerTests
{
    private static TracerProvider NewProvider(string sourceName) =>
        Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .SetSampler(new ParentBasedSampler(new MartenDaemonPollSampler()))
            .Build();

    [Fact]
    public void ShouldSample_HighWaterMarkPoll_DropsTheRootAndItsChildren()
    {
        using var source = new ActivitySource("test-hwm-poll");
        using var provider = NewProvider(source.Name);

        using var poll = source.StartActivity("marten.daemon.highwatermark");
        Assert.NotNull(poll);
        Assert.False(poll.Recorded);

        // The SDK doesn't even create children of a dropped root — nothing reaches the exporters.
        using var databaseQuery = source.StartActivity("db query under the poll");
        Assert.Null(databaseQuery);
    }

    [Fact]
    public void ShouldSample_OtherRoots_RecordsThemAndTheirChildren()
    {
        using var source = new ActivitySource("test-hwm-other");
        using var provider = NewProvider(source.Name);

        using var request = source.StartActivity("GET api/blog/{slug}/comments");
        Assert.NotNull(request);
        Assert.True(request.Recorded);

        using var databaseQuery = source.StartActivity("db query under the request");
        Assert.NotNull(databaseQuery);
        Assert.True(databaseQuery.Recorded);
    }

    [Fact]
    public void ShouldSample_OtherDaemonActivities_RecordsThem()
    {
        using var source = new ActivitySource("test-hwm-daemon");
        using var provider = NewProvider(source.Name);

        using var projectionPage = source.StartActivity("marten.blogcommentnotificationsubscription.all.page.execution");
        Assert.NotNull(projectionPage);
        Assert.True(projectionPage.Recorded);
    }
}
