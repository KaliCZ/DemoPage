using ModelContextProtocol;

namespace Kalandra.McpServer.Infrastructure;

/// <summary>
/// Our own type so <see cref="McpToolErrors"/> never swallows a foreign exception; an <see cref="McpException"/> so the SDK still relays the message if that filter is unwired.
/// </summary>
public sealed class ToolRefusalException(string message) : McpException(message);
