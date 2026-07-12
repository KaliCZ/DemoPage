using System.Security.Cryptography;
using System.Text;
using Kalandra.Blog;

namespace Kalandra.Api.IntegrationTests.Helpers;

/// <summary>
/// Tests mint a fresh random slug per case for stream isolation, so every slug resolves
/// here — with stream ids derived from the slug so each case's streams stay isolated. The
/// production catalog's real gating is covered by BlogPostCatalogTests and the all-posts
/// reaction E2E.
/// </summary>
public sealed class TestBlogPostCatalog : IBlogPostCatalog
{
    public BlogPost? Find(string slug) => new(
        slug,
        CommentsStreamId: DeriveId(slug, "comments"),
        ReactionsStreamId: DeriveId(slug, "reactions"));

    private static Guid DeriveId(string slug, string kind)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes($"{kind}:{slug}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
