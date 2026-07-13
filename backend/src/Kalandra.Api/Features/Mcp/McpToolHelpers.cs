using System.Net.Mail;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Auth;
using ModelContextProtocol;
using StrongTypes;

namespace Kalandra.Api.Features.Mcp;

/// <summary>
/// Shared boundary conversions for MCP tools. Tools receive primitive arguments from the model,
/// so this validates them into the domain's strong types and turns failures into <see cref="McpException"/>s —
/// the MCP equivalent of the controllers' RFC 7807 responses, phrased for a language model to act on.
/// </summary>
internal static class McpToolHelpers
{
    public static CurrentUser RequireUser(ICurrentUserAccessor currentUser) =>
        currentUser.User ?? throw new McpException(
            "This tool needs the user's kalandra.tech account. Connect the MCP server with an " +
            "'Authorization: Bearer <Supabase access token>' header — see https://www.kalandra.tech/mcp.");

    public static NonEmptyString Required(string value, string name) =>
        value.AsNonEmpty() ?? throw new McpException($"'{name}' must not be empty.");

    public static MailAddress ParseEmail(string value) =>
        value.Length <= 254 && MailAddress.TryCreate(value, out var address)
            ? address
            : throw new McpException($"'{value}' is not a valid email address.");
}
