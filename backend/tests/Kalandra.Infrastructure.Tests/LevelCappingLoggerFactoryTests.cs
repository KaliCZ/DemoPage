using Kalandra.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace Kalandra.Infrastructure.Tests;

public class LevelCappingLoggerFactoryTests
{
    private const string CappedPrefix = "Microsoft.Extensions.Diagnostics.HealthChecks";

    private sealed record LogEntry(string Category, LogLevel Level, EventId EventId, Exception? Exception, string Message);

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<LogEntry> Entries { get; } = [];
        public LogLevel MinimumLevel { get; init; } = LogLevel.Trace;

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        private sealed class RecordingLogger(RecordingLoggerFactory factory, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= factory.MinimumLevel;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                factory.Entries.Add(new LogEntry(category, logLevel, eventId, exception, formatter(state, exception)));
        }
    }

    private static LevelCappingLoggerFactory Build(RecordingLoggerFactory inner) =>
        new(inner, CappedPrefix, maximumLevel: LogLevel.Information);

    [Fact]
    public void ErrorInCappedCategory_IsEmittedAsInformation_KeepingEverythingElse()
    {
        var inner = new RecordingLoggerFactory();
        var logger = Build(inner).CreateLogger($"{CappedPrefix}.DefaultHealthCheckService");
        var probeFailure = new TimeoutException("probe timed out");

        logger.Log(LogLevel.Error, new EventId(103, "HealthCheckEnd"), "state", probeFailure, (state, _) => state);

        var entry = Assert.Single(inner.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("HealthCheckEnd", entry.EventId.Name);
        Assert.Same(probeFailure, entry.Exception);
        Assert.Equal("state", entry.Message);
    }

    [Fact]
    public void ErrorOutsideCappedCategory_KeepsItsLevel()
    {
        var inner = new RecordingLoggerFactory();
        var logger = Build(inner).CreateLogger("Kalandra.Api.SomeController");

        logger.LogError("boom");

        Assert.Equal(LogLevel.Error, Assert.Single(inner.Entries).Level);
    }

    [Fact]
    public void LevelsAtOrBelowTheCap_PassThroughUnchanged()
    {
        var inner = new RecordingLoggerFactory();
        var logger = Build(inner).CreateLogger($"{CappedPrefix}.DefaultHealthCheckService");

        logger.LogDebug("begin");
        logger.LogInformation("end");

        Assert.Equal([LogLevel.Debug, LogLevel.Information], inner.Entries.Select(entry => entry.Level));
    }

    [Fact]
    public void IsEnabled_ReflectsTheCappedLevel()
    {
        var inner = new RecordingLoggerFactory { MinimumLevel = LogLevel.Warning };
        var logger = Build(inner).CreateLogger($"{CappedPrefix}.DefaultHealthCheckService");

        // An Error entry would be re-emitted at Information, which this sink has turned off.
        Assert.False(logger.IsEnabled(LogLevel.Error));
    }
}
