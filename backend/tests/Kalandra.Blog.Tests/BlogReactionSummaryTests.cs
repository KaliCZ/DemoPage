using Kalandra.Blog.Entities;
using Kalandra.Blog.Queries;

namespace Kalandra.Blog.Tests;

public class BlogReactionSummaryTests
{
    private const string Slug = "a-post";
    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid VisitorV = new("aaaaaaaa-0000-0000-0000-000000000001");

    private static BlogReaction SignedIn(Guid userId, BlogReactionKind kind, Guid? visitor = null) => new()
    {
        Id = BlogReaction.IdFor(Slug, userId, kind),
        Slug = Slug,
        VisitorId = visitor ?? Guid.NewGuid(),
        UserId = userId,
        Kind = kind,
    };

    private static BlogReaction Anonymous(Guid visitorId, BlogReactionKind kind) => new()
    {
        Id = BlogReaction.IdFor(Slug, visitorId, kind),
        Slug = Slug,
        VisitorId = visitorId,
        Kind = kind,
    };

    [Fact]
    public void CountOf_TalliesEachKindAcrossReactors()
    {
        var summary = BlogReactionSummary.From(
            [SignedIn(UserA, BlogReactionKind.Insightful), SignedIn(UserB, BlogReactionKind.Insightful), SignedIn(UserB, BlogReactionKind.ThumbsDown)],
            VisitorV, UserA);

        Assert.Equal(2, summary.CountOf(BlogReactionKind.Insightful));
        Assert.Equal(1, summary.CountOf(BlogReactionKind.ThumbsDown));
        Assert.Equal(0, summary.CountOf(BlogReactionKind.Heart));
    }

    [Fact]
    public void Mine_ForSignedInUser_MatchesTheirAccount()
    {
        var summary = BlogReactionSummary.From(
            [SignedIn(UserA, BlogReactionKind.Heart), SignedIn(UserB, BlogReactionKind.Rocket)],
            VisitorV, UserA);

        Assert.Equal([BlogReactionKind.Heart], summary.Mine);
    }

    [Fact]
    public void Mine_ForAnonymousReactor_MatchesTheirVisitor()
    {
        var summary = BlogReactionSummary.From(
            [Anonymous(VisitorV, BlogReactionKind.ThumbsUp), SignedIn(UserA, BlogReactionKind.Heart)],
            VisitorV, userId: null);

        Assert.Equal([BlogReactionKind.ThumbsUp], summary.Mine);
    }

    [Fact]
    public void Mine_UnionsTheAccountAndThisBrowsersUnlinkedReaction()
    {
        // Reacted anonymously on this browser, then signed in and reacted again before the link ran.
        var summary = BlogReactionSummary.From(
            [SignedIn(UserA, BlogReactionKind.Heart), Anonymous(VisitorV, BlogReactionKind.Rocket)],
            VisitorV, UserA);

        Assert.Equal([BlogReactionKind.Heart, BlogReactionKind.Rocket], summary.Mine.OrderBy(kind => kind));
    }

    [Fact]
    public void Mine_ExcludesOtherPeoplesReactions()
    {
        var summary = BlogReactionSummary.From([SignedIn(UserB, BlogReactionKind.Heart)], VisitorV, UserA);

        Assert.Empty(summary.Mine);
    }

    [Fact]
    public void IdFor_IsStablePerReactorAndKind_AndDistinctOtherwise()
    {
        // Same reactor + kind keys the same row (so a person's reaction dedupes); anything else differs.
        Assert.Equal(BlogReaction.IdFor(Slug, UserA, BlogReactionKind.Heart), BlogReaction.IdFor(Slug, UserA, BlogReactionKind.Heart));
        Assert.NotEqual(BlogReaction.IdFor(Slug, UserA, BlogReactionKind.Heart), BlogReaction.IdFor(Slug, UserB, BlogReactionKind.Heart));
        Assert.NotEqual(BlogReaction.IdFor(Slug, UserA, BlogReactionKind.Heart), BlogReaction.IdFor(Slug, UserA, BlogReactionKind.Rocket));
    }
}
