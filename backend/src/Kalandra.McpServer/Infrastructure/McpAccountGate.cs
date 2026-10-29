using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Authentication;

namespace Kalandra.McpServer.Infrastructure;

/// <summary>
/// Issues the transport-level 401 challenge that sends an MCP client to sign in or refresh; a tool error would only reach the model.
/// </summary>
public static class McpAccountGate
{
    /// <summary>
    /// Tools served without an account; every other tool acts as the user and its description must carry the <c>[Authorized]</c> marker.
    /// </summary>
    public static readonly IReadOnlySet<string> PublicTools =
        new HashSet<string>(StringComparer.Ordinal) { "list_blog_posts", "get_blog_post_comments" };

    /// <summary>
    /// Middleware, because a tool can't challenge once the response is a JSON-RPC envelope and <c>RequireAuthorization</c> would lock anonymous tools out.
    /// </summary>
    /// <param name="endpointPath">
    /// Scopes the challenge so the discovery documents a challenged client fetches next stay reachable even with a bad token.
    /// </param>
    public static void Use(WebApplication app, string endpointPath) =>
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(endpointPath),
            branch => branch.Use(async (context, next) =>
            {
                if (context.User.Identity?.IsAuthenticated != true && await NeedsAToken(context))
                {
                    await Challenge(context);
                    return;
                }

                await next(context);
            }));

    private static async Task Challenge(HttpContext context)
    {
        await context.ChallengeAsync();

        // The SDK emits only resource_metadata; error="invalid_token" is what tells the client to refresh rather than re-sign-in.
        var details = new List<string>();
        if (context.Request.Headers.ContainsKey(HeaderNames.Authorization))
            details.Add("error=\"invalid_token\"");
        if (SupportedScopes(context) is { Count: > 0 } scopes)
            details.Add($"scope=\"{string.Join(' ', scopes)}\"");

        if (details.Count > 0)
            context.Response.Headers.WWWAuthenticate =
                $"{context.Response.Headers.WWWAuthenticate}, {string.Join(", ", details)}";
    }

    private static IList<string> SupportedScopes(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptionsMonitor<McpAuthenticationOptions>>()
            .Get(McpAuthenticationDefaults.AuthenticationScheme)
            .ResourceMetadata?.ScopesSupported ?? [];

    private static async Task<bool> NeedsAToken(HttpContext context) =>
        // A token that failed to authenticate gets a 401 even for a public tool; only a challenge tells the client to refresh it.
        context.Request.Headers.ContainsKey(HeaderNames.Authorization)
        || await IsAccountToolCall(context);

    private static async Task<bool> IsAccountToolCall(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) != true)
            return false;

        context.Request.EnableBuffering();
        try
        {
            // Fail closed: a tools/call, misshapen ones included, needs an account unless it cleanly names a public tool.
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            return document.RootElement is { ValueKind: JsonValueKind.Object } request
                && request.TryGetProperty("method", out var method)
                && method.ValueKind == JsonValueKind.String
                && method.ValueEquals("tools/call")
                && !NamesAPublicTool(request);
        }
        catch (JsonException)
        {
            // Malformed JSON is the SDK's to reject, with its own protocol error.
            return false;
        }
        finally
        {
            context.Request.Body.Position = 0;
        }
    }

    private static bool NamesAPublicTool(JsonElement request) =>
        request.TryGetProperty("params", out var parameters)
        && parameters.ValueKind == JsonValueKind.Object
        && parameters.TryGetProperty("name", out var name)
        && name.ValueKind == JsonValueKind.String
        && name.GetString() is { } toolName
        && PublicTools.Contains(toolName);
}
