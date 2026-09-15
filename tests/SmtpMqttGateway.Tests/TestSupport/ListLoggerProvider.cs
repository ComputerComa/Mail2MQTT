using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SmtpMqttGateway.Tests.TestSupport;

/// <summary>
/// Captures every formatted log line (including exception text) so tests can
/// assert that secrets never reach the log output.
/// </summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = $"[{category}] {formatter(state, exception)}";
            if (exception is not null)
            {
                line += " " + exception;
            }

            lines.Enqueue(line);
        }
    }
}
