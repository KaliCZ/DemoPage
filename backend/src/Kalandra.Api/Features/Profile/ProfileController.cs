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
    IAvatarService avatarService,
    UploadAvatarHandler uploadAvatarHandler) : ControllerBase
{
    private CurrentUser AppUser => currentUser.RequiredUser;

    [HttpPost("avatar")]
    [RequestSizeLimit(2 * 1024 * 1024)] // Allow slight overhead for multipart framing
    [ProducesResponseType<AvatarResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AvatarResponse>> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await uploadAvatarHandler.HandleAsync(
            AppUser.Id, stream, file.Length, file.ContentType, ct);

        if (result.IsError)
            return this.ValidationError("file", result.Error.Get());

        return new AvatarResponse(AvatarUrl: result.Success.Get());
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
