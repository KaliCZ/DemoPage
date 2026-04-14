namespace Kalandra.Infrastructure.Users;

public record UserPublicInfo(string DisplayName, Uri? AvatarUrl);

public interface IUserInfoService
{
    Task<Dictionary<Guid, UserPublicInfo>> GetUserInfoAsync(IEnumerable<Guid> userIds, CancellationToken ct);

    /// <summary>
    /// Lightweight probe that the backing auth admin API is reachable with the
    /// configured credentials. Throws if not. Used by the /health endpoint.
    /// </summary>
    Task PingAsync(CancellationToken ct);
}
