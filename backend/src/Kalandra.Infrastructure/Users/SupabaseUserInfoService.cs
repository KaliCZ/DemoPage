using System.Net.Http.Headers;
using System.Text.Json;
using Kalandra.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace Kalandra.Infrastructure.Users;

public class SupabaseUserInfoService(
    HttpClient httpClient,
    SupabaseConfig supabaseConfig,
    ILogger<SupabaseUserInfoService> logger) : IUserInfoService
{
    public async Task<Dictionary<Guid, UserPublicInfo>> GetUserInfoAsync(
        IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, UserPublicInfo>();

        foreach (var userId in userIds.Distinct())
        {
            var info = await FetchUserInfoAsync(userId, ct);
            if (info != null)
                result[userId] = info;
        }

        return result;
    }

    private async Task<UserPublicInfo?> FetchUserInfoAsync(Guid userId, CancellationToken ct)
    {
        var projectUrl = supabaseConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = supabaseConfig.ServiceKey.Value;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{projectUrl}/auth/v1/admin/users/{userId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    logger.LogError(
                        "Supabase Admin API GET /admin/users/{UserId} failed. Status: {StatusCode}. Response: {Body}",
                        userId, (int)response.StatusCode, body);
                }
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return Parse(doc.RootElement, userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch user {UserId} from Supabase Auth", userId);
            return null;
        }
    }

    private static UserPublicInfo Parse(JsonElement root, Guid userId)
    {
        Uri? avatarUrl = null;
        string? displayName = null;

        if (root.TryGetProperty("user_metadata", out var metadata) &&
            metadata.ValueKind == JsonValueKind.Object)
        {
            if (metadata.TryGetProperty("avatar_url", out var avatarEl) &&
                avatarEl.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(avatarEl.GetString(), UriKind.Absolute, out var uri))
            {
                avatarUrl = uri;
            }

            if (metadata.TryGetProperty("display_name", out var nameEl) &&
                nameEl.ValueKind == JsonValueKind.String)
            {
                var name = nameEl.GetString();
                if (!string.IsNullOrEmpty(name))
                    displayName = name;
            }

            // Google OAuth stores name in "full_name"
            if (displayName == null &&
                metadata.TryGetProperty("full_name", out var fullNameEl) &&
                fullNameEl.ValueKind == JsonValueKind.String)
            {
                var fullName = fullNameEl.GetString();
                if (!string.IsNullOrEmpty(fullName))
                    displayName = fullName;
            }
        }

        // Fall back to email prefix
        if (displayName == null &&
            root.TryGetProperty("email", out var emailEl) &&
            emailEl.ValueKind == JsonValueKind.String)
        {
            var email = emailEl.GetString();
            if (!string.IsNullOrEmpty(email))
                displayName = email.Split('@')[0];
        }

        displayName ??= userId.ToString();

        return new UserPublicInfo(displayName, avatarUrl);
    }
}
