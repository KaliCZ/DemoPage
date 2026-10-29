using System.Net.Mail;
using Kalandra.Infrastructure.Auth;
using Kalandra.McpServer.Infrastructure;
using StrongTypes;

namespace Kalandra.McpServer.Tools;

/// <summary>
/// Refusals here are the MCP counterpart of the controllers' validation problems, so phrase them for a model to act on.
/// </summary>
internal static class McpToolHelpers
{
    // Backstop only: McpAccountGate already challenges anonymous calls to any tool not listed as public.
    public static CurrentUser RequireUser(ICurrentUserAccessor currentUser) =>
        currentUser.User ?? throw new ToolRefusalException(
            "This tool needs the user's kalandra.tech account. Reconnect and complete the sign-in prompt — see https://www.kalandra.tech/mcp.");

    public static NonEmptyString Required(string value, string name) =>
        value.AsNonEmpty() ?? throw new ToolRefusalException($"'{name}' must not be empty.");

    public static MailAddress ParseEmail(string value) =>
        value.Length <= 254 && MailAddress.TryCreate(value, out var address)
            ? address
            : throw new ToolRefusalException($"'{value}' is not a valid email address.");
}
