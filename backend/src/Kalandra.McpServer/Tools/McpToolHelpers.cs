using System.Net.Mail;
using Kalandra.Infrastructure.Auth;
using ModelContextProtocol;
using StrongTypes;

namespace Kalandra.McpServer.Tools;

/// <summary>
/// Shared boundary conversions for MCP tools. Tools receive primitive arguments from the model,
/// so this validates them into the domain's strong types and turns failures into <see cref="McpException"/>s —
/// the MCP equivalent of the controllers' RFC 7807 responses, phrased for a language model to act on.
/// </summary>
internal static class McpToolHelpers
{
    // McpAccountGate challenges an account tool call before it reaches the tool, so this is the backstop for a
    // tool the gate doesn't know is account-only — it answers the model rather than handing it a null user.
    public static CurrentUser RequireUser(ICurrentUserAccessor currentUser) =>
        currentUser.User ?? throw new McpException(
            "This tool needs the user's kalandra.tech account. Reconnect and complete the sign-in prompt — see https://www.kalandra.tech/mcp.");

    public static NonEmptyString Required(string value, string name) =>
        value.AsNonEmpty() ?? throw new McpException($"'{name}' must not be empty.");

    public static MailAddress ParseEmail(string value) =>
        value.Length <= 254 && MailAddress.TryCreate(value, out var address)
            ? address
            : throw new McpException($"'{value}' is not a valid email address.");
}
