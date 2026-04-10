namespace Kalandra.Infrastructure.Avatars;

public interface IAvatarService
{
    static HashSet<string> AllowedContentTypes { get; } =
        ["image/jpeg", "image/png", "image/webp"];

    const long MaxFileSize = 1 * 1024 * 1024; // 1 MB

    Task<Dictionary<Guid, Uri>> GetAvatarUrlsAsync(
        IEnumerable<Guid> userIds, CancellationToken ct);

    Task<Uri> UploadAvatarAsync(
        Guid userId, Stream content, string contentType, CancellationToken ct);

    Task UpdateAvatarUrlAsync(Guid userId, Uri? avatarUrl, CancellationToken ct);

    Task DeleteAvatarFilesAsync(Guid userId, CancellationToken ct);
}
