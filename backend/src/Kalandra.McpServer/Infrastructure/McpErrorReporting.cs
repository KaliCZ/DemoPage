using ModelContextProtocol;

namespace Kalandra.McpServer.Infrastructure;

public static class McpErrorReporting
{
    // The SDK offers nothing structural to match on — no subtype, and InvalidRequest is also what a malformed
    // request gets — so the wording is the only marker. McpErrorReportingTests pins it against the live SDK.
    private const string AuthorizationRefusedMessage = "Access forbidden: This tool requires authorization.";

    /// <summary>
    /// Recognizes the SDK refusing a tool call that lacked a token — a correct answer to the caller, which the
    /// SDK nonetheless logs at Error and Sentry would otherwise raise as an issue per probing bot.
    /// </summary>
    public static bool IsUnauthorizedToolCall(Exception exception) =>
        exception is McpProtocolException { Message: AuthorizationRefusedMessage };
}
