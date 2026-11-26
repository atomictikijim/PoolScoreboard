using System.Diagnostics;
using System.IO;
using PoolScoreboard.Controller.Diagnostics;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Real files in a throwaway folder, like the other persistence tests: the point of this listener is the
/// file on disk, so asserting against a mock would prove nothing.
/// </summary>
public sealed class TraceLogFileTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-trace-tests", Guid.NewGuid().ToString("n"));

    private string LogPath => Path.Combine(_root, "controller.log");

    [Fact]
    public void Attach_WritesTraceOutputToTheLogFile()
    {
        using (var log = new TraceLogFile(LogPath))
        {
            log.Attach();
            Trace.TraceWarning("Overlay host did not stop within 2s; shutting down anyway.");
        }

        var text = File.ReadAllText(LogPath);

        Assert.Contains("Overlay host did not stop within 2s", text);
        Assert.Contains("Controller started", text);
    }

    [Fact]
    public void Attach_AppendsToAnExistingLogRatherThanReplacingIt()
    {
        using (var first = new TraceLogFile(LogPath))
        {
            first.Attach();
            Trace.TraceWarning("first session warning");
        }

        using (var second = new TraceLogFile(LogPath))
        {
            second.Attach();
            Trace.TraceWarning("second session warning");
        }

        var text = File.ReadAllText(LogPath);

        Assert.Contains("first session warning", text);
        Assert.Contains("second session warning", text);
    }

    [Fact]
    public void Dispose_StopsListeningAndReleasesTheFile()
    {
        var log = new TraceLogFile(LogPath);
        log.Attach();
        Assert.Contains(log, Listeners());

        log.Dispose();

        Assert.DoesNotContain(log, Listeners());
        // Deleting the file proves the writer was closed rather than left holding a handle.
        File.Delete(LogPath);
        Assert.False(File.Exists(LogPath));
    }

    [Fact]
    public void Attach_StaysQuietWhenTheLogCannotBeOpened()
    {
        // A directory where the file should be: opening the stream fails, listening still succeeds.
        Directory.CreateDirectory(LogPath);

        using (var log = new TraceLogFile(LogPath))
        {
            log.Attach();
            Assert.Contains(log, Listeners());
            Trace.TraceWarning("this line has nowhere to go");
        }

        Assert.True(Directory.Exists(LogPath));
    }

    /// <summary><see cref="TraceListenerCollection"/> is not generic, so the assertions need this.</summary>
    private static IEnumerable<TraceListener> Listeners() => Trace.Listeners.Cast<TraceListener>();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
