using System.Security.Cryptography;
using System.Text;

namespace Kalandra.Blog;

/// <summary>
/// Deterministic stream IDs for a post's comment and reaction streams. Separate
/// namespaces keep high-volume reactions off the comment replay path. UUID v5
/// (SHA-1 name-based) derives stable, collision-free IDs from the slug.
/// </summary>
public static class BlogStreamId
{
    private static readonly Guid CommentsNamespace = Guid.Parse("7c9e4a1d-3b6f-4e2a-8d5c-1f0a9b8e7d6c");
    private static readonly Guid ReactionsNamespace = Guid.Parse("2f8b6c4e-9a1d-4c3b-b7e5-6d0f2a8c9e1b");

    public static Guid ForComments(BlogPostSlug slug) => DeriveUuidV5(CommentsNamespace, slug.Value);

    public static Guid ForReactions(BlogPostSlug slug) => DeriveUuidV5(ReactionsNamespace, slug.Value);

    private static Guid DeriveUuidV5(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        var nameBytes = Encoding.UTF8.GetBytes(name);

        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);

        var hash = SHA1.HashData(input);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant RFC 4122

        return new Guid(hash.AsSpan(0, 16));
    }
}
