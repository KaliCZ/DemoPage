namespace Kalandra.Infrastructure.Avatars;

public interface IAvatarService
{
    Task<Dictionary<Guid, Uri>> GetAvatarUrlsAsync(IEnumerable<Guid> userIds, CancellationToken ct);

    /// <summary>
    /// Uploads a new avatar (or overwrites existing) and updates user metadata.
    /// Returns the public URL of the new avatar.
    /// </summary>
    Task<Uri> ReplaceAvatarAsync(Guid userId, Stream content, string contentType, CancellationToken ct);
}
