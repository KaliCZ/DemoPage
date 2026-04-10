namespace Kalandra.Infrastructure.Avatars;

public interface IAvatarService
{
    Task<Dictionary<Guid, Uri>> GetAvatarUrlsAsync(
        IEnumerable<Guid> userIds, CancellationToken ct);

    /// <summary>
    /// Uploads a new avatar, updates user metadata, and deletes old avatar files.
    /// Returns the public URL of the new avatar.
    /// </summary>
    Task<Uri> ReplaceAvatarAsync(
        Guid userId, Stream content, string contentType, CancellationToken ct);

    /// <summary>
    /// Deletes all avatar files and clears the avatar URL from user metadata.
    /// </summary>
    Task RemoveAvatarAsync(Guid userId, CancellationToken ct);
}
