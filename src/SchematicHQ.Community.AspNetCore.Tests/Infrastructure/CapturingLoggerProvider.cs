using Microsoft.Extensions.Logging;

namespace SchematicHQ.Community.AspNetCore.Tests.Infrastructure;

internal sealed record LogEntry(LogLevel Level, string Message);

/// <summary>Collects every log entry written through the logging pipeline it is added to.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToArray();
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose() { }

    private void Add(LogEntry entry)
    {
        lock (_entries)
            _entries.Add(entry);
    }

    private sealed class Logger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
