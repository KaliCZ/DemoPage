using Kalandra.Api.Infrastructure.Auth;
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

    private CurrentUser AppUser => currentUser.CurrentUser;

    [HttpPost("avatar")]
    [RequestSizeLimit(2 * 1024 * 1024)] // Allow slight overhead for multipart framing
    [ProducesResponseType<AvatarResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
            return BadRequest(new { error = "File is empty." });

        if (file.Length > MaxFileSize)
            return BadRequest(new { error = "File size exceeds the 1 MB limit." });

        if (!AllowedContentTypes.Contains(file.ContentType))
            return BadRequest(new { error = "Only JPEG, PNG, and WebP images are allowed." });

        await using var stream = file.OpenReadStream();
        var publicUrl = await userService.UploadAvatarAsync(AppUser.Id, stream, file.ContentType, ct);

        await userService.UpdateAvatarUrlAsync(AppUser.Id, publicUrl, ct);

        return Ok(new AvatarResponse(AvatarUrl: publicUrl));
    }

    [HttpDelete("avatar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        await userService.DeleteAvatarFilesAsync(AppUser.Id, ct);
        await userService.UpdateAvatarUrlAsync(AppUser.Id, avatarUrl: null, ct);

        return NoContent();
    }
}

public record AvatarResponse(string AvatarUrl);
