using Kalandra.Infrastructure.Avatars;
using StrongTypes;

namespace Kalandra.Api.Features.Profile;

public enum UploadAvatarHandlerError { EmptyFile, TooLarge, InvalidContentType }

public class UploadAvatarHandler(IAvatarService avatarService)
{
    private static readonly HashSet<string> AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp"];

    private const long MaxFileSize = 1 * 1024 * 1024; // 1 MB

    public async Task<Try<Uri, UploadAvatarHandlerError>> HandleAsync(
        Guid userId, Stream content, long fileSize, string contentType, CancellationToken ct)
    {
        if (fileSize == 0)
            return Try.Error<Uri, UploadAvatarHandlerError>(UploadAvatarHandlerError.EmptyFile);

        if (fileSize > MaxFileSize)
            return Try.Error<Uri, UploadAvatarHandlerError>(UploadAvatarHandlerError.TooLarge);

        if (!AllowedContentTypes.Contains(contentType))
            return Try.Error<Uri, UploadAvatarHandlerError>(UploadAvatarHandlerError.InvalidContentType);

        var publicUrl = await avatarService.ReplaceAvatarAsync(userId, content, contentType, ct);

        return Try.Success<Uri, UploadAvatarHandlerError>(publicUrl);
    }
}
