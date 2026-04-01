using System.Security.Cryptography;

namespace Kalandra.Api.Features.JobOffers;

/// <summary>
/// Deterministic stream ID for a job offer's comments, separate from the main job offer stream.
/// Uses UUID v5 (SHA-1 name-based) to derive a stable, collision-free ID from the job offer ID.
/// </summary>
public static class CommentStreamId
{
    private static readonly Guid Namespace = Guid.Parse("b6f0a5c2-7e3d-4f1a-9c8b-2d5e6f7a8b9c");

    public static Guid For(Guid jobOfferId)
    {
        var namespaceBytes = Namespace.ToByteArray();
        var nameBytes = jobOfferId.ToByteArray();

        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);

        var hash = SHA1.HashData(input);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant RFC 4122

        return new Guid(hash.AsSpan(0, 16));
    }
}
