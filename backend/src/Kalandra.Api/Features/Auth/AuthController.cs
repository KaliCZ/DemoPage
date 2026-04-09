using Kalandra.Api.Features.Auth.Contracts;
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
    ISupabaseAdminService adminService) : ControllerBase
{
    /// <summary>
    /// Links an email/password identity to the current user's account.
    /// Used when a user signed up via OAuth (e.g. Google) and wants to add email/password login.
    /// </summary>
    [HttpPost("link-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse<LinkEmailError>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LinkEmail(
        [FromBody] LinkEmailRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return BadRequest(new ErrorResponse<LinkEmailError>(LinkEmailError.PasswordTooShort));

        var result = await adminService.ChangePasswordAsync(currentUser.RequiredUser, request.Password, ct);

        if (!result.Success)
            return BadRequest(new ErrorResponse<LinkEmailError>(ClassifyChangePasswordError(result.Error)));

        return NoContent();
    }

    private static LinkEmailError ClassifyChangePasswordError(string? error) =>
        error != null && error.Contains("already", StringComparison.OrdinalIgnoreCase)
            ? LinkEmailError.AlreadyLinked
            : LinkEmailError.Failed;
}
