using Kalandra.Infrastructure.Users;

namespace Kalandra.Api.IntegrationTests.Helpers;

public class NoOpUserInfoService : IUserInfoService
{
    public Task<Dictionary<Guid, UserPublicInfo>> GetUserInfoAsync(
        IEnumerable<Guid> userIds, CancellationToken ct) =>
        Task.FromResult(new Dictionary<Guid, UserPublicInfo>());

    public Task PingAsync(CancellationToken ct) => Task.CompletedTask;
}
