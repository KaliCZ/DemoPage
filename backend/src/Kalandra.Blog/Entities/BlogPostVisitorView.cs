namespace Kalandra.Blog.Entities;

/// <summary>UserId is stamped once the visitor signs in (see LinkVisitor).</summary>
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

    public bool ShouldCountNewView(DateTimeOffset nowUtc, TimeSpan window) => nowUtc - LastViewedAtUtc >= window;
}
