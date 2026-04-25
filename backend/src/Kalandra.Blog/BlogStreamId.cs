using System.Security.Cryptography;
using System.Text;

namespace Kalandra.Blog;

/// <summary>
/// Deterministic UUID v5 stream IDs for a blog post's comments and reactions.
/// Each post slug maps to two independent streams so high-volume reaction
/// events never bloat the comment-replay path (and vice versa).
/// </summary>
public static class BlogStreamId
{
    private static readonly Guid CommentsNamespace = Guid.Parse("3e1f7c2a-9b6d-4f8e-bf0a-1c2d3e4f5061");
    private static readonly Guid ReactionsNamespace = Guid.Parse("8a2b9d4e-7c3f-4f1d-9e2c-5a6b7c8d9e07");

    public static Guid ForComments(NonEmptyString slug) => UuidV5(CommentsNamespace, slug.Value);
    public static Guid ForReactions(NonEmptyString slug) => UuidV5(ReactionsNamespace, slug.Value);

    private static Guid UuidV5(Guid ns, string name)
    {
        var namespaceBytes = ns.ToByteArray();
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
