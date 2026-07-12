using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Marten;

namespace Kalandra.Blog;

public static class MartenConfiguration
{
    /// <summary>
    /// Configures Marten for the Blog domain. Comment and reaction streams are
    /// live-aggregated on read — no snapshots, so state and events cannot drift.
    /// Page views are plain per-(post, visitor) documents rather than a stream:
    /// they are high-volume and the dedup check is a per-visitor point lookup. Slug/UserId
    /// are duplicated for the stats aggregate queries; VisitorId for the sign-in attribution query.
    /// </summary>
    public static void ConfigureBlog(this StoreOptions options)
    {
        options.Events.AddEventType<BlogCommentPosted>();
        options.Events.AddEventType<BlogCommentDeleted>();
        options.Events.AddEventType<BlogReactionAdded>();
        options.Events.AddEventType<BlogReactionRemoved>();
        options.Events.AddEventType<BlogReactorLinked>();
        options.Schema.For<BlogPostVisitorView>()
            .Duplicate(v => v.Slug)
            .Duplicate(v => v.UserId)
            .Duplicate(v => v.VisitorId);
    }
}
