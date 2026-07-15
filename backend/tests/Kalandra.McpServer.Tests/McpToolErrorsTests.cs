using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kalandra.McpServer.Tests;

/// <summary>
/// The tools raise <c>McpException</c> on purpose to tell the model something it can act on, and the SDK logs
/// every one at Error as an unhandled exception — enough to alert on each refused call. McpToolErrors catches
/// them first. Nothing in the response shows whether that still works, so it is pinned here.
/// </summary>
public class McpToolErrorsTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    [Theory]
    // A refused account tool and a bad argument: both are deliberate, both would otherwise alert.
    [InlineData("get_my_comments", "{}", "kalandra.tech account")]
    [InlineData("get_blog_post_comments", """{"slug":"no-such-post"}""", "No blog post with slug")]
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
        // McpProtocolException derives from McpException, so without care the filter would swallow the SDK's
        // own failures too — including InternalError, the one thing that must alert.
        var response = await factory.PostMcp(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"no_such_tool","arguments":{}}}""");

        using var document = await McpServerFactory.ReadJsonRpcResponse(response);
        Assert.True(document.RootElement.TryGetProperty("error", out _), "An unknown tool must stay a JSON-RPC error.");
        Assert.False(document.RootElement.TryGetProperty("result", out _));
    }

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
