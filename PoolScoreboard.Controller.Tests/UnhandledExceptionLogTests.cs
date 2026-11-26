using System.IO;
using PoolScoreboard.Controller.Diagnostics;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The crash path is only useful if the entry is already in the file the operator reads afterwards, so
/// these assert against a real <see cref="TraceLogFile"/> rather than a mock listener.
/// </summary>
public sealed class UnhandledExceptionLogTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-crashlog-tests", Guid.NewGuid().ToString("n"));

    private string LogPath => Path.Combine(_root, "controller.log");

    [Fact]
    public void Record_NamesTheSourceAndTheExceptionWithItsStack()
    {
        var text = Capture(() => UnhandledExceptionLog.RecordUnhandledUi(Thrown("boom")));

        Assert.Contains("Unhandled UI exception", text);
        Assert.Contains("System.InvalidOperationException: boom", text);
        Assert.Contains(nameof(Thrown), text);
    }

    [Fact]
    public void Record_IncludesInnerExceptions()
    {
        var text = Capture(() => UnhandledExceptionLog.RecordUnobservedTask(
            new AggregateException(new IOException("disk gone"))));

        Assert.Contains("Unobserved task exception", text);
        Assert.Contains("System.AggregateException", text);
        Assert.Contains("System.IO.IOException: disk gone", text);
    }

    [Fact]
    public void AppDomainFailure_SkipsTheUiExceptionItAlreadyLogged_ButKeepsAnyOther()
    {
        var uiFailure = Thrown("boom");

        var text = Capture(() =>
        {
            UnhandledExceptionLog.RecordUnhandledUi(uiFailure);
            UnhandledExceptionLog.RecordAppDomainFailure(uiFailure, terminating: true);
            UnhandledExceptionLog.RecordAppDomainFailure(new IOException("other thread"), terminating: true);
        });

        // One crash, one entry even though both handlers saw it...
        Assert.Equal(1, text.Split("System.InvalidOperationException: boom").Length - 1);
        // ...and the skip is not blanket: a different thread's failure still gets its own, labelled entry.
        Assert.Contains("process is terminating: System.IO.IOException: other thread", text);
    }

    private string Capture(Action record)
    {
        using (var log = new TraceLogFile(LogPath))
        {
            log.Attach();
            record();
        }

        return File.ReadAllText(LogPath);
    }

    /// <summary>An exception that really was thrown, so it carries a stack trace.</summary>
    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
