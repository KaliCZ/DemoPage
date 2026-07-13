using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Kalandra.Blog;

namespace Kalandra.Api.IntegrationTests.Helpers;

/// <summary>
/// Tests mint a fresh random slug per case for isolation, so every slug resolves here — with a
/// comment stream id derived from the slug so each case's stream stays isolated. The production
/// catalog's real gating is covered by BlogPostCatalogTests and the all-posts reaction E2E.
/// </summary>
public sealed class TestBlogPostCatalog : IBlogPostCatalog
{
    // The slug→id derivation is one-way, so remember each slug handed out to answer the
    // notification subscription's reverse lookup.
    private readonly ConcurrentDictionary<Guid, string> slugsByStreamId = new();

    public BlogPost? Find(string slug)
    {
        var streamId = DeriveId(slug, "comments");
        slugsByStreamId[streamId] = slug;
        return new BlogPost(slug, streamId);
    }

    public BlogPost? FindByCommentsStreamId(Guid commentsStreamId) =>
        slugsByStreamId.TryGetValue(commentsStreamId, out var slug) ? new BlogPost(slug, commentsStreamId) : null;

    // Slugs are minted per test, so there is no fixed catalog for the background refresher to sweep;
    // tests drive the snapshot for their own slugs directly.
    public IReadOnlyCollection<BlogPost> All => [];

    private static Guid DeriveId(string slug, string kind)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes($"{kind}:{slug}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
