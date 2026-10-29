namespace Kalandra.Blog.Entities;

/// <summary>
/// Keyed by UserId when signed in, else VisitorId, so a person's reaction is one row per (post, kind) across devices; both ids are kept so sign-in can re-key anonymous rows.
/// </summary>
public class BlogReaction
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public Guid VisitorId { get; set; }
    public Guid? UserId { get; set; }
    public BlogReactionKind Kind { get; set; }

    public static string IdFor(string slug, Guid reactorId, BlogReactionKind kind) => $"{slug}:{reactorId}:{kind}";
}
