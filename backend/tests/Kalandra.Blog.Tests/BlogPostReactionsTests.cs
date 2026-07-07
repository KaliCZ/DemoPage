using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;

namespace Kalandra.Blog.Tests;

public class BlogPostReactionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Toggle_WhenInactive_EmitsAdded()
    {
        var reactions = new BlogPostReactions();

        var result = reactions.Toggle(UserA, BlogReactionKind.ThumbsUp, Now);

        var added = Assert.IsType<BlogReactionAdded>(result);
        Assert.Equal(UserA, added.UserId);
        Assert.Equal(BlogReactionKind.ThumbsUp, added.Kind);
        Assert.Equal(Now, added.Timestamp);
    }

    [Fact]
    public void Toggle_WhenActive_EmitsRemoved()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(UserA, BlogReactionKind.Heart, Now));

        var result = reactions.Toggle(UserA, BlogReactionKind.Heart, Now.AddMinutes(1));

        var removed = Assert.IsType<BlogReactionRemoved>(result);
        Assert.Equal(UserA, removed.UserId);
        Assert.Equal(BlogReactionKind.Heart, removed.Kind);
    }

    [Fact]
    public void Toggle_AfterRemoval_EmitsAddedAgain()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(UserA, BlogReactionKind.Rocket, Now));
        reactions.Apply(new BlogReactionRemoved(UserA, BlogReactionKind.Rocket, Now.AddMinutes(1)));

        var result = reactions.Toggle(UserA, BlogReactionKind.Rocket, Now.AddMinutes(2));

        Assert.IsType<BlogReactionAdded>(result);
    }

    [Fact]
    public void Toggle_DifferentKind_IsIndependent()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(UserA, BlogReactionKind.ThumbsUp, Now));

        var result = reactions.Toggle(UserA, BlogReactionKind.ThumbsDown, Now.AddMinutes(1));

        Assert.IsType<BlogReactionAdded>(result);
        Assert.True(reactions.IsActive(UserA, BlogReactionKind.ThumbsUp));
    }

    [Fact]
    public void CountOf_AggregatesAcrossUsers()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(UserA, BlogReactionKind.Insightful, Now));
        reactions.Apply(new BlogReactionAdded(UserB, BlogReactionKind.Insightful, Now));
        reactions.Apply(new BlogReactionAdded(UserB, BlogReactionKind.ThumbsDown, Now));

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
        reactions.Apply(new BlogReactionAdded(UserA, BlogReactionKind.Heart, Now));
        reactions.Apply(new BlogReactionAdded(UserA, BlogReactionKind.Heart, Now.AddSeconds(1)));

        Assert.Equal(1, reactions.CountOf(BlogReactionKind.Heart));
    }

    [Fact]
    public void Apply_RemovedWithoutAdded_IsNoOp()
    {
        var reactions = new BlogPostReactions();

        reactions.Apply(new BlogReactionRemoved(UserA, BlogReactionKind.Heart, Now));

        Assert.Equal(0, reactions.CountOf(BlogReactionKind.Heart));
        Assert.False(reactions.IsActive(UserA, BlogReactionKind.Heart));
    }

    [Fact]
    public void KindsOf_ReturnsOnlyViewersActiveReactions()
    {
        var reactions = new BlogPostReactions();
        reactions.Apply(new BlogReactionAdded(UserA, BlogReactionKind.ThumbsUp, Now));
        reactions.Apply(new BlogReactionAdded(UserA, BlogReactionKind.Rocket, Now));
        reactions.Apply(new BlogReactionAdded(UserB, BlogReactionKind.Heart, Now));
        reactions.Apply(new BlogReactionRemoved(UserA, BlogReactionKind.ThumbsUp, Now.AddMinutes(1)));

        var kinds = reactions.KindsOf(UserA);

        Assert.Equal([BlogReactionKind.Rocket], kinds);
        Assert.Empty(reactions.KindsOf(Guid.NewGuid()));
    }
}
