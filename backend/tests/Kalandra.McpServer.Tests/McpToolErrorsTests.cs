using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Kalandra.McpServer.Infrastructure;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Kalandra.McpServer.Tests;

/// <summary>
/// The tools raise <c>ToolRefusalException</c> on purpose to tell the model something it can act on, and the
/// SDK logs every exception at Error as unhandled — enough to alert on each refused call. McpToolErrors catches
/// ours first, and only ours. Nothing in the response shows whether that still works, so it is pinned here.
/// </summary>
public class McpToolErrorsTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    [Theory]
    // Arguments the model got wrong: deliberate answers it can self-correct from, and every one of them would
    // otherwise alert. (Signing in isn't here — McpAccountGate challenges that before a tool ever runs.)
    [InlineData("get_blog_post_comments", """{"slug":"no-such-post"}""", "No blog post with slug")]
    [InlineData("get_blog_post_comments", """{"slug":""}""", "No blog post with slug")]
    public async Task AToolErrorMeantForTheModel_IsAnswered_AndLoggedNowhere(
        string toolName, string arguments, string expectedText)
    {
        var logs = new CapturingLoggerProvider();
        var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));

        var request = $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{toolName}}}","arguments":{{{arguments}}}}}""";
        var response = await McpServerFactory.PostMcpTo(host, request);

        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
        var result = document.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains(expectedText, result.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString());

        Assert.True(logs.Noise.IsEmpty, $"Sentry alerts on Warning+, but this refusal logged:\n{string.Join("\n", logs.Noise)}");
    }

    [Fact]
    public async Task AProtocolFault_IsLeftToTheSdkToReport()
    {
        // The SDK's own failures — InternalError above all — must keep alerting. Driven straight at the
        // filter: over HTTP every protocol fault worth reaching is either gated or turned into a result.
        var handler = McpToolErrors.ToToolResult(
            (_, _) => throw new McpProtocolException("Boom.", McpErrorCode.InternalError));

        await Assert.ThrowsAsync<McpProtocolException>(async () => await handler(null!, Ct));
    }

    [Fact]
    public async Task SomeoneElsesMcpException_IsLeftToReport()
    {
        // A bare McpException is the SDK's type: anything in the pipeline may throw it, and its message was
        // not authored here for the model — so it is a real failure and belongs in the alerts.
        var handler = McpToolErrors.ToToolResult((_, _) => throw new McpException("Not authored here."));

        await Assert.ThrowsAsync<McpException>(async () => await handler(null!, Ct));
    }

    [Fact]
    public async Task AToolsOwnError_IsAnsweredByTheFilter()
    {
        var handler = McpToolErrors.ToToolResult(
            (_, _) => throw new ToolRefusalException("No blog post with that slug."));

        var result = await handler(null!, Ct);

        Assert.True(result.IsError);
        Assert.Contains("No blog post with that slug.", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Collects what the MCP pipeline reports at the level Sentry turns into issues. Scoped to the SDK's own
    /// categories: unrelated hosting warnings (a CI runner with no DataProtection keys, say) aren't this test's
    /// business.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public readonly ConcurrentBag<string> Noise = [];
        public ILogger CreateLogger(string categoryName) => new Captor(this, categoryName);
        public void Dispose() { }

        private sealed class Captor(CapturingLoggerProvider owner, string category) : ILogger
        {
            private bool IsMcp => category.StartsWith("ModelContextProtocol", StringComparison.Ordinal);

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => IsMcp && logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    owner.Noise.Add($"[{logLevel}] {category}: {formatter(state, exception)}");
            }
        }
    }
}
