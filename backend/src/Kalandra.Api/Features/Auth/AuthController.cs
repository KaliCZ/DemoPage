using System.Diagnostics;
using Kalandra.Api.Features.Auth.Contracts;
using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[Authorize]
public class AuthController(
    ICurrentUserAccessor currentUser,
    ISupabaseAdminService adminService,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("link-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LinkEmail(
        [FromBody] LinkEmailRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return this.ValidationError("password", LinkEmailError.PasswordTooShort);

        var error = await adminService.ChangePasswordAsync(currentUser.RequiredUser, request.Password, ct);
        if (error == null)
            return NoContent();

        switch (error.Code)
        {
            case ChangePasswordErrorCode.AlreadyLinked:
                return this.ValidationError("email", LinkEmailError.AlreadyLinked);
            case ChangePasswordErrorCode.Unknown:
                logger.LogError("LinkEmail failed for user {UserId}: {Message}", currentUser.RequiredUser.Id, error.Message);
                return Problem();
        }

        throw new UnreachableException();
    }
}
