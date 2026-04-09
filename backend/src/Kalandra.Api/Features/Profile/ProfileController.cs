using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.Profile;

[ApiController]
[Route("api/profile")]
[Produces("application/json")]
[Authorize]
public class ProfileController(
    ICurrentUserAccessor currentUser,
    SupabaseUserService userService) : ControllerBase
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
            return ValidationError("file", UploadAvatarError.EmptyFile);

        if (file.Length > MaxFileSize)
            return ValidationError("file", UploadAvatarError.TooLarge);

        if (!AllowedContentTypes.Contains(file.ContentType))
            return ValidationError("file", UploadAvatarError.InvalidContentType);

        await using var stream = file.OpenReadStream();
        var publicUrl = await userService.UploadAvatarAsync(AppUser.Id, stream, file.ContentType, ct);

        await userService.UpdateAvatarUrlAsync(AppUser.Id, publicUrl, ct);

        return new AvatarResponse(AvatarUrl: publicUrl);
    }

    [HttpDelete("avatar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        await userService.DeleteAvatarFilesAsync(AppUser.Id, ct);
        await userService.UpdateAvatarUrlAsync(AppUser.Id, avatarUrl: null, ct);

        return NoContent();
    }

    private ActionResult ValidationError<TError>(string field, TError error) where TError : struct, Enum
    {
        ModelState.AddModelError(field, error.ToString());
        return ValidationProblem();
    }
}

public record AvatarResponse(Uri AvatarUrl);
