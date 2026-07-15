using ModelContextProtocol;

namespace Kalandra.McpServer.Infrastructure;

public static class McpErrorReporting
{
    /// <summary>
    /// Recognizes the protocol errors the caller brought on itself — an unauthorized tool call, an unknown
    /// tool, a malformed request — which the SDK logs at Error even though it already answered them with a
    /// JSON-RPC error. Only <see cref="McpErrorCode.InternalError"/> means this host actually broke.
    /// </summary>
    public static bool IsClientFault(Exception exception) =>
        exception is McpProtocolException { ErrorCode: not McpErrorCode.InternalError };
}
