using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;

namespace Kalandra.Blog.Tests;

public class BlogPostReactionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid VisitorV = new("aaaaaaaa-0000-0000-0000-000000000001");

    [Fact]
    public void Toggle_WhenInactive_EmitsAdded()
    {
        var reactions = new BlogPostReactions();

        var result = reactions.Toggle(VisitorV, UserA, BlogReactionKind.ThumbsUp, Now);

        var added = Assert.IsType<BlogReactionAdded>(result);
        Assert.Equal(VisitorV, added.VisitorId);
        Assert.Equal(UserA, added.UserId);
        Assert.Equal(BlogReactionKind.ThumbsUp, added.Kind);
        Assert.Equal(Now, added.Timestamp);
    }

    [Fact]
    public void Toggle_WhenActive_EmitsRemoved()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(VisitorV, UserA, BlogReactionKind.Heart, Now));

        var result = reactions.Toggle(VisitorV, UserA, BlogReactionKind.Heart, Now.AddMinutes(1));

        var removed = Assert.IsType<BlogReactionRemoved>(result);
        Assert.Equal(UserA, removed.UserId);
        Assert.Equal(BlogReactionKind.Heart, removed.Kind);
    }

    [Fact]
    public void Toggle_Anonymous_IsKeyedByVisitor()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(VisitorV, UserId: null, BlogReactionKind.Rocket, Now));

        Assert.Equal([BlogReactionKind.Rocket], reactions.GetByVisitor(VisitorV));

        var result = reactions.Toggle(VisitorV, userId: null, BlogReactionKind.Rocket, Now.AddMinutes(1));
        Assert.IsType<BlogReactionRemoved>(result);
    }

    [Fact]
    public void SignedIn_And_Anonymous_ReactionsAreTrackedSeparately()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(VisitorV, UserId: null, BlogReactionKind.Heart, Now));
        reactions.Apply(new BlogReactionAdded(Guid.NewGuid(), UserA, BlogReactionKind.Rocket, Now));

        Assert.Equal([BlogReactionKind.Heart], reactions.GetByVisitor(VisitorV));
        Assert.Equal([BlogReactionKind.Rocket], reactions.GetByUser(UserA));
        // A visitor id is not a user id — the two spaces don't leak into each other.
        Assert.Empty(reactions.GetByUser(VisitorV));
        Assert.Equal(2, reactions.TotalCount());
    }

    [Fact]
    public void Toggle_DifferentKind_IsIndependent()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(VisitorV, UserA, BlogReactionKind.ThumbsUp, Now));

        var result = reactions.Toggle(VisitorV, UserA, BlogReactionKind.ThumbsDown, Now.AddMinutes(1));

        Assert.IsType<BlogReactionAdded>(result);
        Assert.Contains(BlogReactionKind.ThumbsUp, reactions.GetByUser(UserA));
    }

    [Fact]
    public void CountOf_AggregatesAcrossReactors()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(VisitorV, UserA, BlogReactionKind.Insightful, Now));
        reactions.Apply(new BlogReactionAdded(Guid.NewGuid(), UserB, BlogReactionKind.Insightful, Now));
        reactions.Apply(new BlogReactionAdded(Guid.NewGuid(), UserB, BlogReactionKind.ThumbsDown, Now));

        Assert.Equal(2, reactions.CountOf(BlogReactionKind.Insightful));
        Assert.Equal(1, reactions.CountOf(BlogReactionKind.ThumbsDown));
        Assert.Equal(0, reactions.CountOf(BlogReactionKind.Heart));
    }

    [Fact]
    public void Apply_DuplicateAdded_StaysIdempotent()
    {
        // Concurrent toggles can race two Added events onto the stream; replay
        // must not double-count them.
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(VisitorV, UserA, BlogReactionKind.Heart, Now));
        reactions.Apply(new BlogReactionAdded(VisitorV, UserA, BlogReactionKind.Heart, Now.AddSeconds(1)));

        Assert.Equal(1, reactions.CountOf(BlogReactionKind.Heart));
    }

    [Fact]
    public void Apply_RemovedWithoutAdded_IsNoOp()
    {
        var reactions = new BlogPostReactions();

        reactions.Apply(new BlogReactionRemoved(VisitorV, UserA, BlogReactionKind.Heart, Now));

        Assert.Equal(0, reactions.CountOf(BlogReactionKind.Heart));
        Assert.Empty(reactions.GetByUser(UserA));
    }

    [Fact]
    public void GetByUser_ReturnsOnlyThatReactorsActiveReactions()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(VisitorV, UserA, BlogReactionKind.ThumbsUp, Now));
        reactions.Apply(new BlogReactionAdded(VisitorV, UserA, BlogReactionKind.Rocket, Now));
        reactions.Apply(new BlogReactionAdded(Guid.NewGuid(), UserB, BlogReactionKind.Heart, Now));
        reactions.Apply(new BlogReactionRemoved(VisitorV, UserA, BlogReactionKind.ThumbsUp, Now.AddMinutes(1)));

        Assert.Equal([BlogReactionKind.Rocket], reactions.GetByUser(UserA));
        Assert.Empty(reactions.GetByUser(Guid.NewGuid()));
    }

    [Fact]
    public void Apply_ReactorLinked_FoldsAnonymousReactionsIntoAccount()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(VisitorV, UserId: null, BlogReactionKind.Heart, Now));

        reactions.Apply(new BlogReactorLinked(VisitorV, UserA, Now.AddMinutes(1)));

        Assert.Empty(reactions.GetByVisitor(VisitorV));
        Assert.Equal([BlogReactionKind.Heart], reactions.GetByUser(UserA));
        Assert.Equal(1, reactions.CountOf(BlogReactionKind.Heart));
    }

    [Fact]
    public void Apply_ReactorLinked_MergingSameKind_DoesNotDoubleCount()
    {
        // Anonymous 👍 then a signed-in 👍 before the link — folding must dedupe to one.
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(VisitorV, UserId: null, BlogReactionKind.ThumbsUp, Now));
        reactions.Apply(new BlogReactionAdded(VisitorV, UserA, BlogReactionKind.ThumbsUp, Now.AddMinutes(1)));

        reactions.Apply(new BlogReactorLinked(VisitorV, UserA, Now.AddMinutes(2)));

        Assert.Equal(1, reactions.CountOf(BlogReactionKind.ThumbsUp));
        Assert.Equal([BlogReactionKind.ThumbsUp], reactions.GetByUser(UserA));
    }
}
