using Microsoft.Extensions.Logging;
using Supabase.Gotrue.Interfaces;

namespace Kalandra.Infrastructure.Users;

public class SupabaseUserInfoService(
    IGotrueAdminClient<Supabase.Gotrue.User> adminAuthClient,
    ILogger<SupabaseUserInfoService> logger) : IUserInfoService
{
    public async Task<Dictionary<Guid, UserPublicInfo>> GetUserInfoAsync(
        IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, UserPublicInfo>();

        foreach (var userId in userIds.Distinct())
        {
            var info = await FetchUserInfoAsync(userId);
            if (info != null)
                result[userId] = info;
        }

        return result;
    }

    private async Task<UserPublicInfo?> FetchUserInfoAsync(Guid userId)
    {
        try
        {
            var user = await adminAuthClient.GetUserById(userId.ToString());
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch user {UserId} from Supabase Auth", userId);
        }

        return null;
    }
}
