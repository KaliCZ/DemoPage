using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Features.Users;

public record UserInfoResponse(string DisplayName, Uri? AvatarUrl);

[ApiController]
[Route("api/users")]
[Produces("application/json")]
[Authorize]
public class UsersController(IUserInfoService userInfoService, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Dictionary<Guid, UserInfoResponse>>(StatusCodes.Status200OK)]
    public async Task<Dictionary<Guid, UserInfoResponse>> GetUsers(
        [FromQuery] Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0)
            return new Dictionary<Guid, UserInfoResponse>();

        var infos = await userInfoService.GetUserInfoAsync(ids, ct);
        return infos.ToDictionary(
            kvp => kvp.Key,
            kvp => new UserInfoResponse(
                DisplayName: kvp.Value.DisplayName,
                AvatarUrl: kvp.Value.AvatarUrl));
    }

    /// <summary>Drops the caller's cached profile after they change their own name or avatar, so it re-resolves on the next read instead of waiting out the cache TTL.</summary>
    [HttpPost("me/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RefreshMe(CancellationToken ct)
    {
        await userInfoService.EvictAsync(currentUser.RequiredUser.Id, ct);
        return NoContent();
    }
}
