using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Avatars;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.Profile;

[ApiController]
[Route("api/profile")]
[Produces("application/json")]
[Authorize]
public class ProfileController(
    ICurrentUserAccessor currentUser,
    IAvatarService avatarService) : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp"];

    private const long MaxFileSize = 1 * 1024 * 1024; // 1 MB

    private CurrentUser AppUser => currentUser.RequiredUser;

    [HttpPost("avatar")]
    [RequestSizeLimit(2 * 1024 * 1024)] // Allow slight overhead for multipart framing
    [ProducesResponseType<AvatarResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AvatarResponse>> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
            return this.ValidationError("file", UploadAvatarError.EmptyFile);

        if (file.Length > MaxFileSize)
            return this.ValidationError("file", UploadAvatarError.TooLarge);

        if (!AllowedContentTypes.Contains(file.ContentType))
            return this.ValidationError("file", UploadAvatarError.InvalidContentType);

        await using var stream = file.OpenReadStream();
        var publicUrl = await avatarService.UploadAvatarAsync(AppUser.Id, stream, file.ContentType, ct);

        await avatarService.UpdateAvatarUrlAsync(AppUser.Id, publicUrl, ct);

        return new AvatarResponse(AvatarUrl: publicUrl);
    }

    [HttpDelete("avatar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        await avatarService.DeleteAvatarFilesAsync(AppUser.Id, ct);
        await avatarService.UpdateAvatarUrlAsync(AppUser.Id, avatarUrl: null, ct);

        return NoContent();
    }
}

public record AvatarResponse(Uri AvatarUrl);
