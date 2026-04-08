using System.Net;
using System.Text.Json;

namespace Kalandra.Api.Tests.Helpers;

/// <summary>
/// Stubs all SupabaseUserService HTTP calls in tests:
/// - GET /auth/v1/admin/users/{id} → empty user_metadata (avatars resolve to null)
/// - PUT /auth/v1/admin/users/{id} → success
/// - POST /storage/v1/object/list/avatars → empty list
/// - POST /storage/v1/object/avatars/{path} → success
/// - DELETE /storage/v1/object/avatars → success
/// </summary>
public class NoOpAvatarHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var method = request.Method;

        // Storage list — return empty array
        if (method == HttpMethod.Post && path.Contains("/storage/v1/object/list/avatars"))
        {
            return Task.FromResult(JsonResponse("[]"));
        }

        // Storage upload (POST avatars/...) — return success
        if (method == HttpMethod.Post && path.Contains("/storage/v1/object/avatars/"))
        {
            return Task.FromResult(JsonResponse("{\"Key\":\"avatars/path\"}"));
        }

        // Storage delete — return success
        if (method == HttpMethod.Delete && path.Contains("/storage/v1/object/avatars"))
        {
            return Task.FromResult(JsonResponse("[]"));
        }

        // Admin update user — return success
        if (method == HttpMethod.Put && path.Contains("/auth/v1/admin/users/"))
        {
            return Task.FromResult(JsonResponse("{}"));
        }

        // Admin get user — return empty user_metadata so avatars resolve to null
        return Task.FromResult(JsonResponse(JsonSerializer.Serialize(new { user_metadata = new { } })));
    }

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
}
