using System.Diagnostics;
using System.IO;
using System.Text;

namespace PoolScoreboard.Controller.Diagnostics;

/// <summary>
/// Appends everything written to <see cref="Trace"/> to a log file, so a warning raised while the app is
/// shutting down — when there is no window left to show it in — can still be read afterwards. The file is
/// opened, appended to and closed per line rather than held open, so the log stays readable by any tool
/// while the app is running and no handle survives a crash.
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
    private bool _disposed;

    /// <summary>
    /// Trace raises a warning as two calls — the "source Warning: id :" prefix through <see cref="Write"/>,
    /// then the message through <see cref="WriteLine"/>. Holding the prefix keeps that one line in the log
    /// instead of a stray half-line above every warning. Bounded, because nothing promises a <c>Write</c>
    /// will be followed by a <c>WriteLine</c>: past this length the prefix is written out on its own rather
    /// than accumulating for the life of the process or gluing itself to an unrelated later line.
    /// </summary>
    private const int PendingFlushLength = 512;

    private string _pending = string.Empty;

    public TraceLogFile(string logPath, long rotateBeyondBytes = DefaultRotateBeyondBytes)
    {
        _logPath = logPath;
        _rotateBeyondBytes = rotateBeyondBytes;
    }

    /// <summary>
    /// Rotates an oversized log out of the way, then starts listening. A log that cannot be opened never
    /// stops the app from starting: diagnostics must not be the reason startup fails, so the listener just
    /// goes quiet.
    /// </summary>
    public void Attach()
    {
        RotateIfTooLarge();

        try
        {
            var folder = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The per-line writes will fail and stay quiet; nothing else to do here.
        }

        Trace.Listeners.Add(this);
        WriteLine("--- Controller started ---");
    }

    public override void Write(string? message)
    {
        if (message == null)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            _pending += message;
            if (_pending.Length >= PendingFlushLength)
            {
                var held = _pending;
                _pending = string.Empty;
                Append(held);
            }
        }
    }

    public override void WriteLine(string? message)
    {
        // Trace serialises listener calls, but this class does not rely on it: rotation and two app
        // instances can share the file, and an append interleaved mid-line would be unreadable.
        lock (_gate)
        {
            if (_disposed)
                return;

            var line = _pending + message;
            _pending = string.Empty;
            Append(line);
        }
    }

    /// <summary>Writes one timestamped line. Callers hold <see cref="_gate"/>.</summary>
    private void Append(string line)
    {
        try
        {
            using var stream = new FileStream(
                _logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing a log line must not take the app down mid-stream.
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Removed before taking _gate on purpose: Trace holds its own global lock while calling a
            // listener, so both paths take the same locks in the same order and cannot deadlock.
            Trace.Listeners.Remove(this);

            lock (_gate)
            {
                // A prefix with no line after it is dropped: half a warning is worse than none.
                _pending = string.Empty;
                _disposed = true;
            }
        }

        base.Dispose(disposing);
    }
}
