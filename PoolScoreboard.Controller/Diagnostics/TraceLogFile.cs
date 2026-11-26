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
    /// <summary>
    /// Roughly a year of warnings for this app: the cap exists so an install that runs for months cannot
    /// accumulate an unbounded file, not to bound any realistic session.
    /// </summary>
    public const long DefaultRotateBeyondBytes = 1 << 20;

    private readonly object _gate = new();
    private readonly string _logPath;
    private readonly long _rotateBeyondBytes;
    private StreamWriter? _writer;

    public TraceLogFile(string logPath, long rotateBeyondBytes = DefaultRotateBeyondBytes)
    {
        _logPath = logPath;
        _rotateBeyondBytes = rotateBeyondBytes;
    }

    /// <summary>
    /// Opens the log for appending and starts listening, first rotating an oversized log out of the way.
    /// A log that cannot be opened never stops the app from starting: diagnostics must not be the reason
    /// startup fails, so the listener just goes quiet.
    /// </summary>
    public void Attach()
    {
        RotateIfTooLarge();

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

    /// <summary>
    /// Moves an oversized log to <c>controller.old.log</c>, replacing whatever previous run was there, so
    /// exactly one earlier session is kept. If the move fails the current log is appended to and simply
    /// stays large — the alternative is losing it.
    /// </summary>
    private void RotateIfTooLarge()
    {
        try
        {
            var file = new FileInfo(_logPath);
            if (!file.Exists || file.Length <= _rotateBeyondBytes)
                return;

            var previous = PreviousLogPath;
            if (File.Exists(previous))
                File.Delete(previous);

            File.Move(_logPath, previous);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Appending to the big log beats both losing it and refusing to start.
        }
    }

    private string PreviousLogPath => Path.Combine(
        Path.GetDirectoryName(_logPath) ?? string.Empty,
        Path.GetFileNameWithoutExtension(_logPath) + ".old" + Path.GetExtension(_logPath));

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
