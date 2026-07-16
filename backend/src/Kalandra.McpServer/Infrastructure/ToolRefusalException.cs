using ModelContextProtocol;

namespace Kalandra.McpServer.Infrastructure;

/// <summary>
/// A deliberate refusal a tool answers the model with — this host's <c>return NotFound()</c>, phrased for the
/// model to act on. Owning the type keeps <see cref="McpToolErrors"/> from ever swallowing an exception that
/// isn't ours; deriving from <see cref="McpException"/> keeps the SDK carrying the message to the model even
/// if that filter is unwired.
/// </summary>
public sealed class ToolRefusalException(string message) : McpException(message);
