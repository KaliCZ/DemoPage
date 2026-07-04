using Kalandra.Blog.Events;
using Marten;

namespace Kalandra.Blog;

public static class MartenConfiguration
{
    /// <summary>
    /// Configures Marten for the Blog domain. Comment and reaction streams are
    /// live-aggregated on read — no snapshots, so state and events cannot drift.
    /// </summary>
    public static void ConfigureBlog(this StoreOptions options)
    {
        options.Events.AddEventType<BlogCommentPosted>();
        options.Events.AddEventType<BlogCommentDeleted>();
        options.Events.AddEventType<BlogReactionAdded>();
        options.Events.AddEventType<BlogReactionRemoved>();
    }
}
