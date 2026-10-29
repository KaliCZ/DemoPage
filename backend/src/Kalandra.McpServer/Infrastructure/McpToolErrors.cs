using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kalandra.McpServer.Infrastructure;

public static class McpToolErrors
{
    /// <summary>
    /// Handles refusals here because the SDK would log each at Error as unhandled and alert on every refused call.
    /// </summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> ToToolResult =>
        next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (ToolRefusalException exception)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = exception.Message }],
                };
            }
        };
}
