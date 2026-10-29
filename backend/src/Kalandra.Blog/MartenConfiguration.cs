using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Kalandra.Blog.Notifications;
using Marten;

namespace Kalandra.Blog;

public static class MartenConfiguration
{
    /// <summary>
    /// Views and reactions are documents rather than streams: they're high-volume and per-reactor, so a row per reactor avoids stream-append contention.
    /// </summary>
    public static void ConfigureBlog(this StoreOptions options)
    {
        options.Events.AddEventType<BlogCommentPosted>();
        options.Events.AddEventType<BlogCommentDeleted>();
        options.Schema.For<BlogNotificationSent>();
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
