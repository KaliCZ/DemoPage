using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Authentication;

namespace Kalandra.McpServer.Infrastructure;

/// <summary>
/// Issues the OAuth challenge the MCP authorization spec requires: a 401 carrying <c>WWW-Authenticate</c> with
/// the resource metadata, which is what sends a client to Supabase to sign in, or to refresh a stale token and
/// retry. The spec puts authorization at the transport level, so this is the only refusal a client's OAuth code
/// acts on — a tool error saying "please sign in" reaches the model, which can do nothing but relay it.
/// </summary>
public static class McpAccountGate
{
    /// <summary>
    /// The tools served to anyone. Everything else acts as the user, so a new tool is account-only until it is
    /// deliberately named here. Mirrored by the <c>[Authorized]</c> marker on the listed descriptions.
    /// </summary>
    public static readonly IReadOnlySet<string> PublicTools =
        new HashSet<string>(StringComparer.Ordinal) { "list_blog_posts", "get_blog_post_comments" };

    /// <summary>
    /// Runs before the MCP endpoint, because the SDK cannot challenge from inside a tool — by then the response
    /// is already a JSON-RPC envelope. Stock <c>RequireAuthorization</c> can't do it either: it would shut the
    /// anonymous tier out of the whole endpoint, and it never sees the body that says which tool is wanted.
    /// </summary>
    /// <param name="endpointPath">
    /// Scopes the challenge to the MCP endpoint. The discovery documents a challenged client is sent to fetch
    /// next must stay reachable, even when it is the token it carries that is the problem.
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

        // The SDK's handler emits only resource_metadata; the spec also wants the scopes named, and RFC 6750
        // wants a rejected token called out — error="invalid_token" is what says refresh, not re-sign-in.
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
        // "Invalid or expired tokens MUST receive a HTTP 401 response" — a presented token that didn't
        // authenticate is stale or forged either way, and only a challenge tells the client to refresh it.
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
            // Fail closed: a tools/call is an account call unless it names a public tool. That covers
            // misshapen calls too (params or name missing or of the wrong kind) — challenging them keeps
            // them out of the SDK, which answers each with a protocol error only after alerting at Error.
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
