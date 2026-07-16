using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kalandra.McpServer.Infrastructure;

public static class McpToolErrors
{
    /// <summary>
    /// This host's <c>UseExceptionHandler</c>: answers the <see cref="ToolRefusalException"/>s the tools raise
    /// on purpose with the isError result the model reads. The SDK would answer them too, but only after logging
    /// each at Error as an unhandled exception, which alerts on every refused call. Anything that isn't our own
    /// type — the SDK's exceptions included — passes through and keeps alerting.
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
