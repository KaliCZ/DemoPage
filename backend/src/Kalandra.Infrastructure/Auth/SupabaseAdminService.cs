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

    public async Task<ChangePasswordError?> ChangePasswordAsync(
        CurrentUser user,
        string password,
        CancellationToken ct)
    {
        var payload = new
        {
            email = user.Email.Address,
            password,
            email_confirm = true,
        };

        var projectUrl = authConfig.ProjectUrl.Value.TrimEnd('/');
        var serviceKey = authConfig.ServiceKey.Value;

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"{projectUrl}/auth/v1/admin/users/{user.Id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("apikey", serviceKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogError(
            "Supabase Admin API PUT /admin/users/{UserId} failed. Status: {StatusCode}. Response: {Body}",
            user.Id,
            (int)response.StatusCode,
            body);

        var errorMessage = TryExtractErrorMessage(body) ?? $"Supabase returned {(int)response.StatusCode}";
        var code = errorMessage.Contains("already", StringComparison.OrdinalIgnoreCase)
            ? ChangePasswordErrorCode.AlreadyLinked
            : ChangePasswordErrorCode.Unknown;

        return new ChangePasswordError(code, errorMessage);
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
