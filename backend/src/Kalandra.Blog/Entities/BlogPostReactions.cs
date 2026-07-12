using Kalandra.Blog.Events;

namespace Kalandra.Blog.Entities;

/// <summary>
/// Live-aggregated state of one post's reaction stream — replayed per read, never snapshotted.
/// Signed-in reactions are grouped by user, anonymous ones by visitor; on sign-in a
/// BlogReactorLinked event folds the visitor's reactions into the account.
/// </summary>
public class BlogPostReactions
{
    public Guid Id { get; set; }
    public Dictionary<Guid, HashSet<BlogReactionKind>> ReactionsByUser { get; set; } = [];
    public Dictionary<Guid, HashSet<BlogReactionKind>> ReactionsByVisitor { get; set; } = [];

    public BlogReactionEvent Toggle(Guid visitorId, Guid? userId, BlogReactionKind kind, DateTimeOffset timestamp) =>
        Current(visitorId, userId).Contains(kind)
            ? new BlogReactionRemoved(visitorId, userId, kind, timestamp)
            : new BlogReactionAdded(visitorId, userId, kind, timestamp);

    public int CountOf(BlogReactionKind kind) =>
        ReactionsByUser.Values.Count(kinds => kinds.Contains(kind))
        + ReactionsByVisitor.Values.Count(kinds => kinds.Contains(kind));

    public int TotalCount() =>
        ReactionsByUser.Values.Sum(kinds => kinds.Count)
        + ReactionsByVisitor.Values.Sum(kinds => kinds.Count);

    public IReadOnlyCollection<BlogReactionKind> GetByUser(Guid userId) =>
        ReactionsByUser.GetValueOrDefault(userId) ?? [];

    public IReadOnlyCollection<BlogReactionKind> GetByVisitor(Guid visitorId) =>
        ReactionsByVisitor.GetValueOrDefault(visitorId) ?? [];

    public void Apply(BlogReactionAdded e) => Bucket(e.VisitorId, e.UserId).Add(e.Kind);

    public void Apply(BlogReactionRemoved e) => Current(e.VisitorId, e.UserId).Remove(e.Kind);

    public void Apply(BlogReactorLinked e)
    {
        if (!ReactionsByVisitor.Remove(e.VisitorId, out var anonymous))
            return;
        if (!ReactionsByUser.TryGetValue(e.UserId, out var account))
            ReactionsByUser[e.UserId] = account = [];
        account.UnionWith(anonymous);
    }

    private HashSet<BlogReactionKind> Current(Guid visitorId, Guid? userId) =>
        userId is { } uid
            ? ReactionsByUser.GetValueOrDefault(uid) ?? []
            : ReactionsByVisitor.GetValueOrDefault(visitorId) ?? [];

    private HashSet<BlogReactionKind> Bucket(Guid visitorId, Guid? userId)
    {
        var (map, key) = userId is { } uid ? (ReactionsByUser, uid) : (ReactionsByVisitor, visitorId);
        if (!map.TryGetValue(key, out var kinds))
            map[key] = kinds = [];
        return kinds;
    }
}
