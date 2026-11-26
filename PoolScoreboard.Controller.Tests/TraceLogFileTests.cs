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

    private string PreviousLogPath => Path.Combine(_root, "controller.old.log");

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
    public void Attach_LeavesTheLogReadableWhileItIsStillAttached()
    {
        using (var log = new TraceLogFile(LogPath))
        {
            log.Attach();
            Trace.TraceWarning("readable while running");

            // The ordinary reader a person or tool would use: FileShare.Read, which fails against a held
            // writer. This is the whole point of appending per line.
            var lines = File.ReadAllLines(LogPath);
            // Exactly one line, and it carries Trace's "source Warning: id :" prefix: the prefix and the
            // message are merged, not a stray half-line above every warning.
            var warning = Assert.Single(lines.Where(line => line.Contains("readable while running")));
            Assert.Contains("Warning:", warning);
            Assert.StartsWith("20", warning);
        }
    }

    [Fact]
    public void WriteWithoutALineBreak_IsFlushedInsteadOfHeldForever()
    {
        var fragment = new string('z', 600);

        using (var log = new TraceLogFile(LogPath))
        {
            log.Attach();
            log.Write(fragment);

            // A Write with no WriteLine behind it still has to reach the file rather than sitting in memory.
            Assert.Contains(fragment, File.ReadAllText(LogPath));

            Trace.TraceWarning("later warning");
        }

        // And the flushed fragment must not glue itself onto the next warning.
        var warning = File.ReadAllLines(LogPath).Single(line => line.Contains("later warning"));
        Assert.DoesNotContain("zzz", warning);
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
                    // Deleting the file proves no handle is left holding it.
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

    [Fact]
    public void Attach_RotatesAnOversizedLogToThePreviousRunFile()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(LogPath, new string('x', 200) + "old session tail");

        using (var log = new TraceLogFile(LogPath, rotateBeyondBytes: 64))
        {
            log.Attach();
            Trace.TraceWarning("new session warning");
        }

        Assert.Contains("old session tail", File.ReadAllText(PreviousLogPath));
        var current = File.ReadAllText(LogPath);
        Assert.Contains("new session warning", current);
        Assert.DoesNotContain("old session tail", current);
    }

    [Fact]
    public void Attach_KeepsOnlyTheImmediatelyPreviousRun()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(PreviousLogPath, "two sessions back");
        File.WriteAllText(LogPath, new string('y', 200) + "last session tail");

        using (var log = new TraceLogFile(LogPath, rotateBeyondBytes: 64))
            log.Attach();

        var previous = File.ReadAllText(PreviousLogPath);
        Assert.Contains("last session tail", previous);
        Assert.DoesNotContain("two sessions back", previous);
    }

    [Fact]
    public void Attach_LeavesALogUnderTheCapInPlace()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(LogPath, "small session tail");

        using (var log = new TraceLogFile(LogPath, rotateBeyondBytes: 64))
            log.Attach();

        Assert.Contains("small session tail", File.ReadAllText(LogPath));
        Assert.False(File.Exists(PreviousLogPath));
    }

    /// <summary><see cref="TraceListenerCollection"/> is not generic, so the assertions need this.</summary>
    private static IEnumerable<TraceListener> Listeners() => Trace.Listeners.Cast<TraceListener>();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
