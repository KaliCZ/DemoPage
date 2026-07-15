using ModelContextProtocol;

namespace Kalandra.McpServer.Infrastructure;

public static class McpErrorReporting
{
    /// <summary>
    /// Recognizes the errors the tools raise deliberately to tell the model something it can act on — not
    /// signed in, no such slug, invalid email. They are this host's RFC 7807 equivalent, and no more an
    /// incident than the REST API's 400s; the SDK just happens to log every tool exception at Error.
    /// </summary>
    public static bool IsToolErrorForTheCaller(Exception exception) => exception is McpException;
}
