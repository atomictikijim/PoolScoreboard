using System.Diagnostics;
using System.IO;
using System.Text;

namespace PoolScoreboard.Controller.Diagnostics;

/// <summary>
/// Appends everything written to <see cref="Trace"/> to a log file, so a warning raised while the app is
/// shutting down — when there is no window left to show it in — can still be read afterwards.
/// </summary>
public sealed class TraceLogFile : TraceListener
{
    private readonly object _gate = new();
    private readonly string _logPath;
    private StreamWriter? _writer;

    public TraceLogFile(string logPath) => _logPath = logPath;

    /// <summary>
    /// Opens the log for appending and starts listening. A log that cannot be opened never stops the app
    /// from starting: diagnostics must not be the reason startup fails, so the listener just goes quiet.
    /// </summary>
    public void Attach()
    {
        try
        {
            var folder = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            _writer = new StreamWriter(
                new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            _writer.WriteLine($"--- Controller started {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ---");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _writer = null;
        }

        Trace.Listeners.Add(this);
    }

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message)
    {
        lock (_gate)
        {
            if (_writer == null || message == null)
                return;

            try
            {
                _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Losing a log line must not take the app down mid-stream. A disposed writer cannot be
                // reached here: Dispose takes _gate to null it, and this write holds _gate throughout.
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Removed before taking _gate on purpose: Trace holds its own global lock while calling a
            // listener, so both paths take the same locks in the same order and cannot deadlock.
            Trace.Listeners.Remove(this);

            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        base.Dispose(disposing);
    }
}
