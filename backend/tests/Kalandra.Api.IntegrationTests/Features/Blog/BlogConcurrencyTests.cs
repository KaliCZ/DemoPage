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
    public async Task DifferentReactorsOnSamePost_DoNotConflict()
    {
        // Reactions are rows keyed by the reactor, so two people reacting to the same post at
        // once write different rows — no stream-append version clash, no exception.
        var slug = Guid.NewGuid().ToString();

        await using var firstSession = store.LightweightSession();
        await using var secondSession = store.LightweightSession();
        firstSession.Store(Reaction(slug, reactorId: Guid.NewGuid(), BlogReactionKind.Heart));
        secondSession.Store(Reaction(slug, reactorId: Guid.NewGuid(), BlogReactionKind.Heart));

        await firstSession.SaveChangesAsync(Ct);
        await secondSession.SaveChangesAsync(Ct);

        await using var verifySession = store.LightweightSession();
        var hearts = await verifySession.Query<BlogReaction>()
            .Where(r => r.Slug == slug && r.Kind == BlogReactionKind.Heart).CountAsync(Ct);
        Assert.Equal(2, hearts);
    }

    [Fact]
    public async Task SameReactorRacingSameKind_ConvergesToOneRow()
    {
        // The same reactor from two browsers keys the same row id, so racing writers upsert to a
        // single reaction instead of double-counting — and neither throws.
        var slug = Guid.NewGuid().ToString();
        var reactorId = Guid.NewGuid();

        await using var firstSession = store.LightweightSession();
        await using var secondSession = store.LightweightSession();
        firstSession.Store(Reaction(slug, reactorId, BlogReactionKind.Heart));
        secondSession.Store(Reaction(slug, reactorId, BlogReactionKind.Heart));

        await firstSession.SaveChangesAsync(Ct);
        await secondSession.SaveChangesAsync(Ct);

        await using var verifySession = store.LightweightSession();
        var hearts = await verifySession.Query<BlogReaction>()
            .Where(r => r.Slug == slug && r.Kind == BlogReactionKind.Heart).CountAsync(Ct);
        Assert.Equal(1, hearts);
    }

    private static BlogReaction Reaction(string slug, Guid reactorId, BlogReactionKind kind) => new()
    {
        Id = BlogReaction.IdFor(slug, reactorId, kind),
        Slug = slug,
        VisitorId = reactorId,
        Kind = kind,
    };
}
