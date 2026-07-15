using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kalandra.McpServer.Infrastructure;

public static class McpToolErrors
{
    /// <summary>
    /// This host's <c>UseExceptionHandler</c>: answers the <see cref="McpException"/>s the tools raise on
    /// purpose with the isError result the model reads. The SDK does this too, but only after logging it at
    /// Error as an unhandled exception, which alerts on every refused call.
    /// </summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> ToToolResult =>
        next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            // Only the messages the tools author for the model travel back. McpProtocolException derives from
            // McpException, so it has to be stepped around: a protocol fault or an InternalError is someone
            // else's text and a real failure, and belongs in the alerts with everything else we don't catch.
            catch (McpException exception) when (exception is not McpProtocolException)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = exception.Message }],
                };
            }
        };
}
