using Kalandra.Infrastructure.Users;

namespace Kalandra.Api.IntegrationTests.Helpers;

/// <summary>Returns only the profiles a test seeds into <see cref="Profiles"/>; empty by default, so comments keep the author fields captured at post time.</summary>
public class FakeUserInfoService : IUserInfoService
{
    public Dictionary<Guid, UserPublicInfo> Profiles { get; } = new();

    public Task<Dictionary<Guid, UserPublicInfo>> GetUserInfoAsync(
        IEnumerable<Guid> userIds, CancellationToken ct) =>
        Task.FromResult(userIds.Distinct()
            .Where(Profiles.ContainsKey)
            .ToDictionary(id => id, id => Profiles[id]));

    public Task PingAsync(CancellationToken ct) => Task.CompletedTask;
}
