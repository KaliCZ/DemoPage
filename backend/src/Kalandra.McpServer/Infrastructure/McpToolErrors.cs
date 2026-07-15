using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kalandra.McpServer.Infrastructure;

public static class McpToolErrors
{
    /// <summary>
    /// This host's <c>UseExceptionHandler</c>: turns the <see cref="McpException"/>s the tools raise on purpose
    /// — not signed in, no such slug, invalid email — into the isError result the model reads, the MCP shape of
    /// the API's RFC 7807 responses. The SDK would do the same, but only after logging it at Error as an
    /// unhandled exception; catching it first, inside that outer handler, keeps a refused call out of the alerts.
    /// </summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> ToToolResult =>
        next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (McpException exception)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = exception.Message }],
                };
            }
        };
}
