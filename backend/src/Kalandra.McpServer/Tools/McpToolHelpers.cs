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
    // The account gate: every [Authorized] tool opens with this. The tools are listed to everyone so a model
    // can see what the site offers, so this — not a tools/list omission — is what keeps them account-only.
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
