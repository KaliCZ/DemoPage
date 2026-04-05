using System.Collections.Concurrent;
using System.Text.Json;
using Kalandra.Infrastructure.Auth;

namespace Kalandra.Api.Tests.Helpers;

public class FakeSupabaseAdminService : ISupabaseAdminService
{
    private readonly ConcurrentDictionary<string, JsonElement> _users = new();

    /// <summary>
    /// Seeds a user with the given identities for test setup.
    /// </summary>
    public void SeedUser(string userId, string email, params string[] providers)
    {
        var identities = providers.Select(p => new
        {
            id = Guid.NewGuid().ToString(),
            provider = p,
            identity_data = new { email },
            created_at = DateTimeOffset.UtcNow.ToString("o"),
            updated_at = DateTimeOffset.UtcNow.ToString("o"),
        }).ToArray();

        var user = JsonSerializer.SerializeToElement(new
        {
            id = userId,
            email,
            identities,
            app_metadata = new
            {
                provider = providers.FirstOrDefault() ?? "email",
                providers,
            },
        });

        _users[userId] = user;
    }

    public Task<SupabaseAdminResult> UpdateUserAsync(
        string userId,
        object updatePayload,
        CancellationToken ct)
    {
        if (!_users.TryGetValue(userId, out var user))
            return Task.FromResult(new SupabaseAdminResult(Success: false, Error: "User not found"));

        // Simulate adding an email identity
        var payloadJson = JsonSerializer.SerializeToElement(updatePayload);
        if (payloadJson.TryGetProperty("password", out _))
        {
            var email = user.GetProperty("email").GetString()!;
            var existingIdentities = user.GetProperty("identities")
                .EnumerateArray().ToList();

            var hasEmail = existingIdentities.Any(i =>
                i.GetProperty("provider").GetString() == "email");

            if (!hasEmail)
            {
                // Rebuild user with email identity added
                var allIdentities = existingIdentities
                    .Select(i => new
                    {
                        id = i.GetProperty("id").GetString()!,
                        provider = i.GetProperty("provider").GetString()!,
                        identity_data = new { email },
                        created_at = i.GetProperty("created_at").GetString()!,
                        updated_at = i.GetProperty("updated_at").GetString()!,
                    })
                    .Append(new
                    {
                        id = Guid.NewGuid().ToString(),
                        provider = "email",
                        identity_data = new { email },
                        created_at = DateTimeOffset.UtcNow.ToString("o"),
                        updated_at = DateTimeOffset.UtcNow.ToString("o"),
                    })
                    .ToArray();

                var providers = allIdentities.Select(i => i.provider).ToArray();

                var updated = JsonSerializer.SerializeToElement(new
                {
                    id = userId,
                    email,
                    identities = allIdentities,
                    app_metadata = new
                    {
                        provider = user.GetProperty("app_metadata")
                            .GetProperty("provider").GetString(),
                        providers,
                    },
                });

                _users[userId] = updated;
            }
        }

        return Task.FromResult(new SupabaseAdminResult(Success: true));
    }

    public Task<JsonElement?> GetUserAsync(string userId, CancellationToken ct)
    {
        return _users.TryGetValue(userId, out var user)
            ? Task.FromResult<JsonElement?>(user)
            : Task.FromResult<JsonElement?>(null);
    }

    public Task<SupabaseAdminResult> DeleteIdentityAsync(
        string identityId,
        CancellationToken ct)
    {
        foreach (var (userId, user) in _users)
        {
            var identities = user.GetProperty("identities").EnumerateArray().ToList();
            var target = identities.FirstOrDefault(i =>
                i.GetProperty("id").GetString() == identityId);

            if (target.ValueKind != JsonValueKind.Undefined)
            {
                var email = user.GetProperty("email").GetString()!;
                var remaining = identities
                    .Where(i => i.GetProperty("id").GetString() != identityId)
                    .Select(i => new
                    {
                        id = i.GetProperty("id").GetString(),
                        provider = i.GetProperty("provider").GetString(),
                        identity_data = new { email },
                        created_at = i.GetProperty("created_at").GetString(),
                        updated_at = i.GetProperty("updated_at").GetString(),
                    })
                    .ToArray();

                var providers = remaining.Select(i => i.provider).ToArray();

                var updated = JsonSerializer.SerializeToElement(new
                {
                    id = userId,
                    email,
                    identities = remaining,
                    app_metadata = new
                    {
                        provider = providers.FirstOrDefault() ?? "",
                        providers,
                    },
                });

                _users[userId] = updated;
                return Task.FromResult(new SupabaseAdminResult(Success: true));
            }
        }

        return Task.FromResult(new SupabaseAdminResult(
            Success: false,
            Error: "Identity not found"));
    }
}
