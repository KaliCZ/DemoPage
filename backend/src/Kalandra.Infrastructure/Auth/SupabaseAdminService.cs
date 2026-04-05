using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kalandra.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace Kalandra.Infrastructure.Auth;

public class SupabaseAdminService(
    HttpClient httpClient,
    SupabaseAuthConfig authConfig,
    ILogger<SupabaseAdminService> logger) : ISupabaseAdminService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<SupabaseAdminResult> UpdateUserAsync(
        string userId,
        object updatePayload,
        CancellationToken ct)
    {
        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = authConfig.ServiceKey.Value;

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"{projectUrl}/auth/v1/admin/users/{Uri.EscapeDataString(userId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(updatePayload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
            return new SupabaseAdminResult(Success: true);

        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogError(
            "Supabase Admin API PUT /admin/users/{UserId} failed. Status: {StatusCode}. Response: {Body}",
            userId,
            (int)response.StatusCode,
            body);

        var errorMessage = TryExtractErrorMessage(body) ?? $"Supabase returned {(int)response.StatusCode}";
        return new SupabaseAdminResult(Success: false, Error: errorMessage);
    }

    public async Task<JsonElement?> GetUserAsync(string userId, CancellationToken ct)
    {
        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = authConfig.ServiceKey.Value;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{projectUrl}/auth/v1/admin/users/{Uri.EscapeDataString(userId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);

        using var response = await httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Supabase Admin API GET /admin/users/{UserId} failed. Status: {StatusCode}. Response: {Body}",
                userId,
                (int)response.StatusCode,
                body);
            return null;
        }

        var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    public async Task<SupabaseAdminResult> DeleteIdentityAsync(
        string identityId,
        CancellationToken ct)
    {
        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = authConfig.ServiceKey.Value;

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{projectUrl}/auth/v1/admin/identities/{Uri.EscapeDataString(identityId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);

        using var response = await httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
            return new SupabaseAdminResult(Success: true);

        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogError(
            "Supabase Admin API DELETE /admin/identities/{IdentityId} failed. Status: {StatusCode}. Response: {Body}",
            identityId,
            (int)response.StatusCode,
            body);

        var errorMessage = TryExtractErrorMessage(body) ?? $"Supabase returned {(int)response.StatusCode}";
        return new SupabaseAdminResult(Success: false, Error: errorMessage);
    }

    private static string? TryExtractErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("msg", out var msg))
                return msg.GetString();
            if (doc.RootElement.TryGetProperty("message", out var message))
                return message.GetString();
            if (doc.RootElement.TryGetProperty("error_description", out var desc))
                return desc.GetString();
        }
        catch
        {
            // Not JSON, ignore
        }

        return null;
    }
}
