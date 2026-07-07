namespace Kalandra.Infrastructure.Users;

public record UserPublicInfo(string DisplayName, Uri? AvatarUrl);

public interface IUserInfoService
{
    Task<Dictionary<Guid, UserPublicInfo>> GetUserInfoAsync(IEnumerable<Guid> userIds, CancellationToken ct);

    /// <summary>Drops any cached copy of a user's profile — call after the user changes their own name or avatar.</summary>
    Task EvictAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Lightweight probe that the backing auth admin API is reachable with the
    /// configured credentials. Throws if not. Used by the /health endpoint.
    /// </summary>
    Task PingAsync(CancellationToken ct);
}
