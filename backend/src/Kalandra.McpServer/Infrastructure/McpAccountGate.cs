using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Net.Http.Headers;

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
                    await context.ChallengeAsync();
                    return;
                }

                await next(context);
            }));

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
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            return document.RootElement is { ValueKind: JsonValueKind.Object } request
                && request.TryGetProperty("method", out var method)
                && method.ValueKind == JsonValueKind.String
                && method.ValueEquals("tools/call")
                && request.TryGetProperty("params", out var parameters)
                && parameters.TryGetProperty("name", out var name)
                && name.GetString() is { } toolName
                && !PublicTools.Contains(toolName);
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
}
