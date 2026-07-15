using Kalandra.McpServer.Infrastructure;
using ModelContextProtocol;

namespace Kalandra.McpServer.Tests;

/// <summary>
/// Pins which MCP failures are worth an alert. The SDK logs every refused call at Error, which would otherwise
/// make a bot probing an account tool look like an outage.
/// </summary>
public class McpErrorReportingTests
{
    [Theory]
    // InvalidRequest is what the SDK's authorization filter throws for a tool call without a valid token.
    [InlineData(McpErrorCode.InvalidRequest)]
    [InlineData(McpErrorCode.InvalidParams)]
    [InlineData(McpErrorCode.MethodNotFound)]
    [InlineData(McpErrorCode.ParseError)]
    public void AProtocolErrorTheCallerCaused_IsAClientFault(McpErrorCode errorCode) =>
        Assert.True(McpErrorReporting.IsClientFault(new McpProtocolException("Refused.", errorCode)));

    [Fact]
    public void AProtocolErrorFromThisHostBreaking_IsNotAClientFault() =>
        Assert.False(McpErrorReporting.IsClientFault(new McpProtocolException("Boom.", McpErrorCode.InternalError)));

    [Fact]
    public void AnExceptionThatIsNotAProtocolError_IsNotAClientFault() =>
        Assert.False(McpErrorReporting.IsClientFault(new InvalidOperationException("Boom.")));
}
