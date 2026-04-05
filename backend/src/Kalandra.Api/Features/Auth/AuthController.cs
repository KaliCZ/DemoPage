using System.Text.Json;
using Kalandra.Api.Features.Auth.Contracts;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
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
    [Authorize]
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

    /// <summary>
    /// Returns the current user's identity providers.
    /// </summary>
    [HttpGet("identities")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIdentities(CancellationToken ct)
    {
        var user = await adminService.GetUserAsync(AppUser.Id, ct);
        if (user == null)
            return NotFound();

        var identities = user.Value.TryGetProperty("identities", out var ids)
            ? ids.EnumerateArray().Select(i => new
            {
                id = i.GetProperty("id").GetString(),
                provider = i.GetProperty("provider").GetString(),
                createdAt = i.TryGetProperty("created_at", out var ca) ? ca.GetString() : null,
                updatedAt = i.TryGetProperty("updated_at", out var ua) ? ua.GetString() : null,
            }).ToList()
            : [];

        return Ok(new { identities });
    }

    /// <summary>
    /// Unlinks an identity provider from the current user's account.
    /// </summary>
    [HttpDelete("identities/{identityId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlinkIdentity(
        string identityId,
        CancellationToken ct)
    {
        // Verify the identity belongs to the current user
        var user = await adminService.GetUserAsync(AppUser.Id, ct);
        if (user == null)
            return NotFound();

        var identities = user.Value.TryGetProperty("identities", out var ids)
            ? ids.EnumerateArray().ToList()
            : [];

        var targetIdentity = identities.FirstOrDefault(i =>
            i.GetProperty("id").GetString() == identityId);

        if (targetIdentity.ValueKind == JsonValueKind.Undefined)
            return NotFound(new { error = "Identity not found on this account." });

        if (identities.Count <= 1)
            return BadRequest(new { error = "Cannot remove the last identity. At least one login method must remain." });

        var result = await adminService.DeleteIdentityAsync(identityId, ct);

        if (!result.Success)
            return BadRequest(new { error = result.Error ?? "Failed to unlink identity." });

        return Ok(new { message = "Identity unlinked successfully." });
    }
}
