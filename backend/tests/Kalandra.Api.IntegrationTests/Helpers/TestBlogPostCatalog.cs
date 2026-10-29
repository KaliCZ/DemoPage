using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Kalandra.Blog;

namespace Kalandra.Api.IntegrationTests.Helpers;

/// <summary>Resolves every slug, since tests mint a fresh random slug per case for isolation.</summary>
public sealed class TestBlogPostCatalog : IBlogPostCatalog
{
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
