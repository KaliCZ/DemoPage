using System.Net;
using System.Text.Json;

namespace Kalandra.Api.Tests.Helpers;

/// <summary>
/// Returns an empty user_metadata for any Supabase Admin API user lookup,
/// so SupabaseUserService resolves all avatars to null in tests.
/// </summary>
public class NoOpAvatarHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { user_metadata = new { } }),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        return Task.FromResult(response);
    }
}
