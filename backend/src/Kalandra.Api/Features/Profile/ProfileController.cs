using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.Profile;

[ApiController]
[Route("api/profile")]
[Produces("application/json")]
[Authorize]
public class ProfileController(
    ICurrentUserAccessor currentUser,
    UploadAvatarHandler uploadAvatarHandler,
    DeleteAvatarHandler deleteAvatarHandler) : ControllerBase
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
        {
            var error = result.Error.Get();
            return error switch
            {
                UploadAvatarHandlerError.EmptyFile =>
                    this.ValidationError("file", UploadAvatarError.EmptyFile),
                UploadAvatarHandlerError.TooLarge =>
                    this.ValidationError("file", UploadAvatarError.TooLarge),
                UploadAvatarHandlerError.InvalidContentType =>
                    this.ValidationError("file", UploadAvatarError.InvalidContentType),
            };
        }

        return new AvatarResponse(AvatarUrl: result.Success.Get());
    }

    [HttpDelete("avatar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        await deleteAvatarHandler.HandleAsync(AppUser.Id, ct);

        return NoContent();
    }
}

public record AvatarResponse(Uri AvatarUrl);
