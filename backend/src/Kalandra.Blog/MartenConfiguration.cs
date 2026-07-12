using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Marten;

namespace Kalandra.Blog;

public static class MartenConfiguration
{
    /// <summary>
    /// Configures Marten for the Blog domain. Comments are an event stream, live-aggregated on
    /// read — no snapshots, so state and events cannot drift. Views and reactions are plain
    /// documents rather than streams: both are high-volume and per-reactor, so a row keyed by the
    /// reactor sidesteps stream-append contention and makes counts a point/aggregate query. The
    /// duplicated columns back the stats and sign-in attribution queries.
    /// </summary>
    public static void ConfigureBlog(this StoreOptions options)
    {
        options.Events.AddEventType<BlogCommentPosted>();
        options.Events.AddEventType<BlogCommentDeleted>();
        options.Schema.For<BlogPostVisitorView>()
            .Duplicate(v => v.Slug)
            .Duplicate(v => v.UserId)
            .Duplicate(v => v.VisitorId);
        options.Schema.For<BlogReaction>()
            .Duplicate(r => r.Slug)
            .Duplicate(r => r.UserId)
            .Duplicate(r => r.VisitorId)
            .Duplicate(r => r.Kind);
    }
}
