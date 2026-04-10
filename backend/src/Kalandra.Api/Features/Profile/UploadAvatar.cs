using Kalandra.Infrastructure.Avatars;
using StrongTypes;

namespace Kalandra.Api.Features.Profile;

public class UploadAvatarHandler(IAvatarService avatarService)
{
    private static readonly HashSet<string> AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp"];

    private const long MaxFileSize = 1 * 1024 * 1024; // 1 MB

    public async Task<Try<Uri, UploadAvatarError>> HandleAsync(
        Guid userId, Stream content, long fileSize, string contentType, CancellationToken ct)
    {
        if (fileSize == 0)
            return Try.Error<Uri, UploadAvatarError>(UploadAvatarError.EmptyFile);

        if (fileSize > MaxFileSize)
            return Try.Error<Uri, UploadAvatarError>(UploadAvatarError.TooLarge);

        if (!AllowedContentTypes.Contains(contentType))
            return Try.Error<Uri, UploadAvatarError>(UploadAvatarError.InvalidContentType);

        var publicUrl = await avatarService.UploadAvatarAsync(userId, content, contentType, ct);
        await avatarService.UpdateAvatarUrlAsync(userId, publicUrl, ct);

        return Try.Success<Uri, UploadAvatarError>(publicUrl);
    }
}
