using Kalandra.Infrastructure.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using Supabase.Gotrue.Mfa;
using Supabase.Gotrue.Responses;

namespace Kalandra.Infrastructure.Tests;

public class SupabaseUserInfoServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Stand-in Gotrue admin client whose GetUserById is scripted per test; nothing else is called.</summary>
    private sealed class FakeGotrueAdminClient : IGotrueAdminClient<User>
    {
        public required Func<string, Task<User?>> OnGetUserById { get; init; }

        public Task<User?> GetUserById(string userId) => OnGetUserById(userId);

        public Func<Dictionary<string, string>>? GetHeaders { get; set; }

        public Task<User?> CreateUser(AdminUserAttributes attributes) => throw new NotImplementedException();
        public Task<User?> CreateUser(string email, string password, AdminUserAttributes? attributes = null) => throw new NotImplementedException();
        public Task<bool> DeleteUser(string uid) => throw new NotImplementedException();
        public Task<User?> GetUser(string jwt) => throw new NotImplementedException();
        public Task<bool> InviteUserByEmail(string email, InviteUserByEmailOptions? options = null) => throw new NotImplementedException();
        public Task<UserList<User>?> ListUsers(string? filter = null, string? sortBy = null, Constants.SortOrder sortOrder = Constants.SortOrder.Descending, int? page = null, int? perPage = null) => throw new NotImplementedException();
        public Task<User?> Update(UserAttributes attributes) => throw new NotImplementedException();
        public Task<User?> UpdateUserById(string userId, AdminUserAttributes userData) => throw new NotImplementedException();
        public Task<GenerateLinkResponse?> GenerateLink(GenerateLinkOptions options) => throw new NotImplementedException();
        public Task<MfaAdminListFactorsResponse?> ListFactors(MfaAdminListFactorsParams listFactorsParams) => throw new NotImplementedException();
        public Task<MfaAdminDeleteFactorResponse?> DeleteFactor(MfaAdminDeleteFactorParams deleteFactorParams) => throw new NotImplementedException();
    }

    private static SupabaseUserInfoService Build(Func<string, Task<User?>> onGetUserById, TimeSpan? fetchTimeout = null) =>
        new(new FakeGotrueAdminClient { OnGetUserById = onGetUserById },
            NullLogger<SupabaseUserInfoService>.Instance, fetchTimeout);

    private static User UserWithMetadata(Guid id, string displayName, string? avatarUrl) => new()
    {
        Id = id.ToString(),
        Email = $"{displayName.ToLowerInvariant()}@example.com",
        UserMetadata = avatarUrl is null
            ? new Dictionary<string, object> { ["display_name"] = displayName }
            : new Dictionary<string, object> { ["display_name"] = displayName, ["avatar_url"] = avatarUrl },
    };

    [Fact]
    public async Task ResponsiveFetch_MapsNameAndAvatarFromMetadata()
    {
        var userId = Guid.NewGuid();
        var service = Build(_ => Task.FromResult<User?>(UserWithMetadata(userId, "Ada", "https://cdn.test/ada.png")));

        var result = await service.GetUserInfoAsync([userId], Ct);

        Assert.Equal("Ada", result[userId].DisplayName);
        Assert.Equal(new Uri("https://cdn.test/ada.png"), result[userId].AvatarUrl);
    }

    [Fact]
    public async Task HungFetch_TimesOutAndOmitsTheProfile()
    {
        var service = Build(_ => new TaskCompletionSource<User?>().Task, fetchTimeout: TimeSpan.FromMilliseconds(50));

        var result = await service.GetUserInfoAsync([Guid.NewGuid()], Ct);

        Assert.Empty(result);
    }

    [Fact]
    public async Task MixedResponsiveAndHungFetches_ReturnTheResponsiveProfile()
    {
        var responsive = Guid.NewGuid();
        var hung = Guid.NewGuid();
        var service = Build(
            id => id == responsive.ToString()
                ? Task.FromResult<User?>(UserWithMetadata(responsive, "Ada", avatarUrl: null))
                : new TaskCompletionSource<User?>().Task,
            fetchTimeout: TimeSpan.FromMilliseconds(50));

        var result = await service.GetUserInfoAsync([responsive, hung], Ct);

        Assert.Equal("Ada", result[responsive].DisplayName);
        Assert.False(result.ContainsKey(hung));
    }

    [Fact]
    public async Task CancelledCaller_PropagatesInsteadOfDegradingToAMissingProfile()
    {
        var service = Build(_ => new TaskCompletionSource<User?>().Task);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetUserInfoAsync([Guid.NewGuid()], cancelled.Token));
    }

    // Pins the API DI factory's assumption: ActivatorUtilities can construct this service without a registered timeout.
    [Fact]
    public void DiActivation_SucceedsWithoutARegisteredTimeout()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGotrueAdminClient<User>>(
            new FakeGotrueAdminClient { OnGetUserById = _ => Task.FromResult<User?>(null) });
        services.AddSingleton<ILogger<SupabaseUserInfoService>>(NullLogger<SupabaseUserInfoService>.Instance);
        using var provider = services.BuildServiceProvider();

        var service = ActivatorUtilities.CreateInstance<SupabaseUserInfoService>(provider);

        Assert.NotNull(service);
    }
}
