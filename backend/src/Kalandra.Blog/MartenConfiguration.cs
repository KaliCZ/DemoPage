using JasperFx.Events.Projections;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Marten;

namespace Kalandra.Blog;

public static class MartenConfiguration
{
    /// <summary>
    /// Configures Marten for the Blog domain. Comment and reaction streams are
    /// live-aggregated on read — no snapshots, so state and events cannot drift.
    /// Read streams gain an event per page view and are queried in bulk by the
    /// stats endpoint, so they get an inline snapshot instead of per-read replay.
    /// </summary>
    public static void ConfigureBlog(this StoreOptions options)
    {
        options.Events.AddEventType<BlogCommentPosted>();
        options.Events.AddEventType<BlogCommentDeleted>();
        options.Events.AddEventType<BlogReactionAdded>();
        options.Events.AddEventType<BlogReactionRemoved>();
        options.Events.AddEventType<BlogPostRead>();
        options.Projections.Snapshot<BlogPostReads>(SnapshotLifecycle.Inline);
    }
}
