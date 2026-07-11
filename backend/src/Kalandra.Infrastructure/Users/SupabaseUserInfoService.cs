using Kalandra.Infrastructure.Tasks;
using Microsoft.Extensions.Logging;
using Supabase.Gotrue.Interfaces;

namespace Kalandra.Infrastructure.Users;

public class SupabaseUserInfoService(
    IGotrueAdminClient<Supabase.Gotrue.User> adminAuthClient,
    ILogger<SupabaseUserInfoService> logger,
    TimeSpan? fetchTimeout = null) : IUserInfoService
{
    private readonly TimeSpan _fetchTimeout = fetchTimeout ?? TimeSpan.FromSeconds(3);

    public async Task PingAsync(CancellationToken ct)
    {
        // perPage=1 keeps the round-trip minimal; we only need to confirm the key is accepted.
        await adminAuthClient.ListUsers(
            filter: null,
            sortBy: null,
            sortOrder: Supabase.Gotrue.Constants.SortOrder.Descending,
            page: null,
            perPage: 1);
    }

    public async Task<Dictionary<Guid, UserPublicInfo>> GetUserInfoAsync(
        IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var distinct = userIds.Distinct().ToArray();
        var infos = await Task.WhenAll(distinct.Select(userId => FetchUserInfoAsync(userId, ct)));

        var result = new Dictionary<Guid, UserPublicInfo>();
        for (var i = 0; i < distinct.Length; i++)
            if (infos[i] is { } info)
                result[distinct[i]] = info;

        return result;
    }

    // No cache here — eviction is the caching decorator's job.
    public Task EvictAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;

    private async Task<UserPublicInfo?> FetchUserInfoAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            // Gotrue's admin client ignores cancellation, so the timeout is enforced here — a hung Supabase must cost a missing profile, not a stalled page.
            var user = await adminAuthClient.GetUserById(userId.ToString()).WaitObservedAsync(_fetchTimeout, ct);
            if (user == null)
                return null;

            Uri? avatarUrl = null;
            string? displayName = null;

            if (user.UserMetadata is { } metadata)
            {
                if (metadata.TryGetValue("avatar_url", out var avatarObj) &&
                    avatarObj is string url &&
                    Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    avatarUrl = uri;
                }

                if (metadata.TryGetValue("display_name", out var nameObj) &&
                    nameObj is string name &&
                    !string.IsNullOrEmpty(name))
                {
                    displayName = name;
                }

                // Google OAuth stores name in "full_name"
                if (displayName == null &&
                    metadata.TryGetValue("full_name", out var fullNameObj) &&
                    fullNameObj is string fullName &&
                    !string.IsNullOrEmpty(fullName))
                {
                    displayName = fullName;
                }
            }

            // Fall back to email prefix
            displayName ??= user.Email?.Split('@')[0] ?? userId.ToString();

            return new UserPublicInfo(displayName, avatarUrl);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch user {UserId} from Supabase Auth", userId);
        }

        return null;
    }
}
