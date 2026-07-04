using Kalandra.Blog.Events;

namespace Kalandra.Blog.Entities;

/// <summary>
/// Live-aggregated state of one post's reaction stream — replayed per read, never snapshotted.
/// </summary>
public class BlogPostReactions
{
    public Guid Id { get; set; }
    public Dictionary<Guid, HashSet<BlogReactionKind>> ReactionsByUser { get; set; } = [];

    public BlogReactionEvent Toggle(Guid userId, BlogReactionKind kind, DateTimeOffset timestamp) =>
        IsActive(userId, kind)
            ? new BlogReactionRemoved(userId, kind, timestamp)
            : new BlogReactionAdded(userId, kind, timestamp);

    public bool IsActive(Guid userId, BlogReactionKind kind) =>
        ReactionsByUser.TryGetValue(userId, out var kinds) && kinds.Contains(kind);

    public int CountOf(BlogReactionKind kind) =>
        ReactionsByUser.Values.Count(kinds => kinds.Contains(kind));

    public IReadOnlyCollection<BlogReactionKind> KindsOf(Guid userId) =>
        ReactionsByUser.TryGetValue(userId, out var kinds) ? kinds : [];

    public void Apply(BlogReactionAdded e)
    {
        if (!ReactionsByUser.TryGetValue(e.UserId, out var kinds))
            ReactionsByUser[e.UserId] = kinds = [];
        kinds.Add(e.Kind);
    }

    public void Apply(BlogReactionRemoved e)
    {
        if (ReactionsByUser.TryGetValue(e.UserId, out var kinds))
            kinds.Remove(e.Kind);
    }
}
