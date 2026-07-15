using Kalandra.McpServer.Infrastructure;
using ModelContextProtocol;

namespace Kalandra.McpServer.Tests;

/// <summary>
/// Pins which MCP failures are worth an alert. The SDK logs a refused tool call at Error, which would make
/// every bot probing an account tool look like an outage — but only that refusal may be silenced.
/// </summary>
public class McpErrorReportingTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    [Fact]
    public async Task TheRefusalTheLiveSdkActuallyThrows_IsRecognized()
    {
        // Round-trips the SDK's own wording back through the filter: reworded by an upgrade, this fails here
        // instead of quietly refilling Sentry.
        var response = await factory.PostMcp(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_my_comments","arguments":{}}}""");

        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
        var message = document.RootElement.GetProperty("error").GetProperty("message").GetString()!;

        Assert.True(McpErrorReporting.IsUnauthorizedToolCall(new McpProtocolException(message, McpErrorCode.InvalidRequest)));
    }

    [Fact]
    public void AMalformedRequest_StillReports() =>
        Assert.False(McpErrorReporting.IsUnauthorizedToolCall(
            new McpProtocolException("Request is invalid.", McpErrorCode.InvalidRequest)));

    [Fact]
    public void AnUnknownToolName_StillReports() =>
        Assert.False(McpErrorReporting.IsUnauthorizedToolCall(
            new McpProtocolException("Unknown tool 'nope'.", McpErrorCode.InvalidParams)));

    [Fact]
    public void ThisHostBreaking_StillReports() =>
        Assert.False(McpErrorReporting.IsUnauthorizedToolCall(
            new McpProtocolException("Boom.", McpErrorCode.InternalError)));

    [Fact]
    public void AnExceptionThatIsNotAProtocolError_StillReports() =>
        Assert.False(McpErrorReporting.IsUnauthorizedToolCall(new InvalidOperationException("Boom.")));
}
