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
    // Tests hand out slugs on demand; remember each resolved post so both the notification
    // subscription's reverse lookup and cross-post "my comments" queries can see it.
    private readonly ConcurrentDictionary<string, BlogPost> resolved = new(StringComparer.Ordinal);

    public BlogPost? Find(string slug) => resolved.GetOrAdd(slug, s => new BlogPost(
        s,
        CommentsStreamId: DeriveId(s, "comments")));

    public BlogPost? FindByCommentsStreamId(Guid commentsStreamId) =>
        resolved.Values.FirstOrDefault(post => post.CommentsStreamId == commentsStreamId);

    public IReadOnlyCollection<BlogPost> All => [.. resolved.Values];

    private static Guid DeriveId(string slug, string kind)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes($"{kind}:{slug}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
