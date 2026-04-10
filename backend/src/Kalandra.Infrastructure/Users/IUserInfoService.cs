namespace Kalandra.Infrastructure.Users;

public record UserPublicInfo(string DisplayName, Uri? AvatarUrl);

public interface IUserInfoService
{
    Task<Dictionary<Guid, UserPublicInfo>> GetUserInfoAsync(IEnumerable<Guid> userIds, CancellationToken ct);
}
