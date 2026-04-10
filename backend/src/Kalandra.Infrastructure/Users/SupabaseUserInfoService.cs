using Kalandra.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Supabase;

namespace Kalandra.Infrastructure.Users;

public class SupabaseUserInfoService(
    Client supabase,
    SupabaseAuthConfig authConfig,
    ILogger<SupabaseUserInfoService> logger) : IUserInfoService
{
    private readonly Supabase.Gotrue.Interfaces.IGotrueAdminClient<Supabase.Gotrue.User> adminAuthClient =
        supabase.AdminAuth(authConfig.ServiceKey.Value);

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
                    !string.IsNullOrEmpty(url) &&
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
