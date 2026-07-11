using Microsoft.Extensions.Logging;

namespace Kalandra.Infrastructure.Logging;

/// <summary>
/// Re-emits log entries of one category at a capped severity, for framework categories that
/// log expected operational states at Error/Warning — the entries stay in every sink, but
/// below error-alerting thresholds.
/// </summary>
public sealed class LevelCappingLoggerFactory(ILoggerFactory inner, string categoryPrefix, LogLevel maximumLevel) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) =>
        categoryName.StartsWith(categoryPrefix, StringComparison.Ordinal)
            ? new LevelCappingLogger(inner.CreateLogger(categoryName), maximumLevel)
            : inner.CreateLogger(categoryName);

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose() => inner.Dispose();

    private sealed class LevelCappingLogger(ILogger inner, LogLevel maximumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(Cap(logLevel));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(Cap(logLevel), eventId, state, exception, formatter);

        private LogLevel Cap(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel > maximumLevel ? maximumLevel : logLevel;
    }
}
