using Kalandra.Infrastructure.Configuration;
using Kalandra.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Supabase;

namespace Kalandra.Infrastructure.Users;

public class SupabaseUserInfoService(
    Client supabase,
    SupabaseConfig supabaseConfig,
    IStorageService storageService,
    ILogger<SupabaseUserInfoService> logger) : IUserInfoService
{
    private readonly Supabase.Gotrue.Interfaces.IGotrueAdminClient<Supabase.Gotrue.User> adminAuthClient =
        supabase.AdminAuth(supabaseConfig.ServiceKey.Value);

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
                    !string.IsNullOrEmpty(url))
                {
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        avatarUrl = uri;
                    }
                    else
                    {
                        // Treat as a storage path and resolve via the storage service
                        var publicUrl = storageService.GetPublicUrl(url);
                        Uri.TryCreate(publicUrl, UriKind.Absolute, out avatarUrl);
                    }
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
