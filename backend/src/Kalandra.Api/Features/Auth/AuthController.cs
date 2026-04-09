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
    private CurrentUser AppUser => currentUser.CurrentUser;

    /// <summary>
    /// Links an email/password identity to the current user's account.
    /// Used when a user signed up via OAuth (e.g. Google) and wants to add email/password login.
    /// </summary>
    [HttpPost("link-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LinkEmail(
        [FromBody] LinkEmailRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return BadRequest(new { error = "Password must be at least 6 characters." });

        var result = await adminService.UpdateUserAsync(
            userId: AppUser.Id,
            updatePayload: new { email = AppUser.Email, password = request.Password, email_confirm = true },
            ct);

        if (!result.Success)
            return BadRequest(new { error = result.Error ?? "Failed to link email identity." });

        return Ok(new { message = "Email/password identity linked successfully." });
    }
}
