namespace Kalandra.Blog.Entities;

/// <summary>
/// One reactor's reaction of a single kind on a post. Keyed by the reactor identity
/// (UserId when signed in, else VisitorId), so a person's reaction collapses to one row per
/// (post, kind) across devices and the count dedupes by construction. UserId and VisitorId are
/// stored too, so the reactor's origin stays legible and sign-in can re-key the anonymous rows.
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
