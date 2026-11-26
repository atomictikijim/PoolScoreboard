using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PoolScoreboard.Overlay.Diagnostics;

/// <summary>
/// Sends the overlay host's own log records — Kestrel bind failures, faults while serving a request, an
/// unhandled exception in an endpoint — to <see cref="Trace"/>, which is what the Controller's file listener
/// records. Without this they land only in the Console and Debug providers, and a WPF process has no console
/// to write them to, so a control-API failure left no evidence anywhere.
/// </summary>
public sealed class TraceLoggerProvider : ILoggerProvider
{
    /// <summary>Warnings and up: the level the host already logs at, and the only one worth a file line.</summary>
    private const LogLevel MinimumLevel = LogLevel.Warning;

    public ILogger CreateLogger(string categoryName) => new TraceLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class TraceLogger : ILogger
    {
        private readonly string _category;

        public TraceLogger(string category) => _category = category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter == null)
                return;

            var message = $"{_category}: {formatter(state, exception)}";
            if (exception != null)
                message += $" [{exception.GetType().Name}: {exception.Message}]";

            if (logLevel >= LogLevel.Error)
                Trace.TraceError(message);
            else
                Trace.TraceWarning(message);
        }
    }
}
