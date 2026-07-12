using Kalandra.Blog.Events;

namespace Kalandra.Blog.Entities;

/// <summary>
/// Live-aggregated state of one post's reaction stream — replayed per read, never snapshotted.
/// Reactions are keyed by the reactor: the signed-in UserId when known, otherwise the
/// anonymous VisitorId. A BlogReactorLinked event folds a visitor's reactions into the account.
/// </summary>
public class BlogPostReactions
{
    public Guid Id { get; set; }
    public Dictionary<Guid, HashSet<BlogReactionKind>> ReactionsByReactor { get; set; } = [];

    private static Guid Reactor(Guid visitorId, Guid? userId) => userId ?? visitorId;

    public BlogReactionEvent Toggle(Guid visitorId, Guid? userId, BlogReactionKind kind, DateTimeOffset timestamp) =>
        IsActive(Reactor(visitorId, userId), kind)
            ? new BlogReactionRemoved(visitorId, userId, kind, timestamp)
            : new BlogReactionAdded(visitorId, userId, kind, timestamp);

    public bool IsActive(Guid reactorId, BlogReactionKind kind) =>
        ReactionsByReactor.TryGetValue(reactorId, out var kinds) && kinds.Contains(kind);

    public int CountOf(BlogReactionKind kind) =>
        ReactionsByReactor.Values.Count(kinds => kinds.Contains(kind));

    public int TotalCount() =>
        ReactionsByReactor.Values.Sum(kinds => kinds.Count);

    public IReadOnlyCollection<BlogReactionKind> KindsOf(Guid reactorId) =>
        ReactionsByReactor.TryGetValue(reactorId, out var kinds) ? kinds : [];

    public void Apply(BlogReactionAdded e)
    {
        var reactor = Reactor(e.VisitorId, e.UserId);
        if (!ReactionsByReactor.TryGetValue(reactor, out var kinds))
            ReactionsByReactor[reactor] = kinds = [];
        kinds.Add(e.Kind);
    }

    public void Apply(BlogReactionRemoved e)
    {
        if (ReactionsByReactor.TryGetValue(Reactor(e.VisitorId, e.UserId), out var kinds))
            kinds.Remove(e.Kind);
    }

    public void Apply(BlogReactorLinked e)
    {
        if (!ReactionsByReactor.Remove(e.VisitorId, out var anonymous))
            return;
        if (!ReactionsByReactor.TryGetValue(e.UserId, out var account))
            ReactionsByReactor[e.UserId] = account = [];
        account.UnionWith(anonymous);
    }
}
