namespace Kalandra.Blog.Entities;

/// <summary>
/// One visitor's view record for one post — a plain document keyed by
/// "{slug}:{visitorId}", not an event stream. Page views are high-volume and the
/// dedup check is a per-visitor point lookup, so a row per (post, visitor) beats
/// folding a stream. UserId is stamped once the visitor signs in (see LinkVisitor).
/// </summary>
public class BlogPostVisitorView
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public Guid VisitorId { get; set; }
    public Guid? UserId { get; set; }
    public int ViewCount { get; set; }
    public DateTimeOffset FirstViewedAtUtc { get; set; }
    public DateTimeOffset LastViewedAtUtc { get; set; }

    public static string IdFor(string slug, Guid visitorId) => $"{slug}:{visitorId}";

    /// <summary>A refresh inside the window is the same visit; a return after it is a new one.</summary>
    public bool ShouldCountNewView(DateTimeOffset nowUtc, TimeSpan window) => nowUtc - LastViewedAtUtc >= window;
}
