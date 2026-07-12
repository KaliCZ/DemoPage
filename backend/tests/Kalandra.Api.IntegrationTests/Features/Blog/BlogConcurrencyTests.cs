using JasperFx;
using JasperFx.Events;
using Kalandra.Api.IntegrationTests.Helpers;
using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using StrongTypes;

namespace Kalandra.Api.IntegrationTests.Features.Blog;

public class BlogConcurrencyTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly IDocumentStore store = factory.Services.GetRequiredService<IDocumentStore>();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BlogCommentPosted NewComment(string content) => new(
        CommentId: Guid.NewGuid(),
        ParentCommentId: null,
        UserId: Guid.NewGuid(),
        UserEmail: Email.Create("commenter@test.com"),
        AuthorDisplayName: "Commenter".ToNonEmpty(),
        AuthorAvatarUrl: null,
        Content: content.ToNonEmpty(),
        Timestamp: DateTimeOffset.UtcNow);

    [Fact]
    public async Task ExpectedVersionAppend_RejectsStaleSecondWriter_OnTheCommentStream()
    {
        var streamId = Guid.NewGuid();

        await using (var seedSession = store.LightweightSession())
        {
            seedSession.Events.Append(streamId, NewComment("Seed"));
            await seedSession.SaveChangesAsync(Ct);
        }

        await using var firstSession = store.LightweightSession();
        await using var secondSession = store.LightweightSession();

        // Both writers expect to produce version 2 of the stream; only one can.
        firstSession.Events.Append(streamId, 2, NewComment("First writer"));
        secondSession.Events.Append(streamId, 2, NewComment("Stale second writer"));

        await firstSession.SaveChangesAsync(Ct);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => secondSession.SaveChangesAsync(Ct));
        Assert.True(exception is ConcurrencyException or EventStreamUnexpectedMaxEventIdException);
    }

    [Fact]
    public async Task ReactionAndCommentStreams_DoNotConflict()
    {
        // A post's comment and reaction streams are two distinct ids; interleaved writers
        // on them must not collide — a reaction lands between the comment writer's version
        // check and its save.
        var commentsStreamId = Guid.NewGuid();
        var reactionsStreamId = Guid.NewGuid();

        await using var commentSession = store.LightweightSession();
        commentSession.Events.Append(commentsStreamId, 1, NewComment("Hello"));

        await using (var reactionSession = store.LightweightSession())
        {
            reactionSession.Events.Append(
                reactionsStreamId,
                new BlogReactionAdded(VisitorId: Guid.NewGuid(), UserId: Guid.NewGuid(), Kind: BlogReactionKind.Heart, Timestamp: DateTimeOffset.UtcNow));
            await reactionSession.SaveChangesAsync(Ct);
        }

        await commentSession.SaveChangesAsync(Ct);

        await using var verifySession = store.LightweightSession();
        var comments = await verifySession.Events.AggregateStreamAsync<BlogPostComments>(commentsStreamId, token: Ct);
        var reactions = await verifySession.Events.AggregateStreamAsync<BlogPostReactions>(reactionsStreamId, token: Ct);
        Assert.Single(comments!.Comments);
        Assert.Equal(1, reactions!.CountOf(BlogReactionKind.Heart));
    }

    [Fact]
    public async Task RacingDuplicateReactionAppends_ConvergeOnReplay()
    {
        // Reaction toggles deliberately use plain appends (no stream lock): two racing
        // "add Heart" writers both commit, and the idempotent Apply makes replay converge
        // to a single reaction instead of double-counting.
        var streamId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var firstSession = store.LightweightSession();
        await using var secondSession = store.LightweightSession();
        // Same reactor (userId), different browsers — replay must still converge to one reaction.
        firstSession.Events.Append(streamId, new BlogReactionAdded(Guid.NewGuid(), userId, BlogReactionKind.Heart, DateTimeOffset.UtcNow));
        secondSession.Events.Append(streamId, new BlogReactionAdded(Guid.NewGuid(), userId, BlogReactionKind.Heart, DateTimeOffset.UtcNow));

        await firstSession.SaveChangesAsync(Ct);
        await secondSession.SaveChangesAsync(Ct);

        await using var verifySession = store.LightweightSession();
        var events = await verifySession.Events.FetchStreamAsync(streamId, token: Ct);
        Assert.Equal(2, events.Count);

        var reactions = await verifySession.Events.AggregateStreamAsync<BlogPostReactions>(streamId, token: Ct);
        Assert.Equal(1, reactions!.CountOf(BlogReactionKind.Heart));
    }
}
