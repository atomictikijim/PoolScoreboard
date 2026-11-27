using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using PoolScoreboard.Controller.Diagnostics;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The rule that lets the overlay take its port back from a leftover copy of this app: only a copy that shows no
/// window, never this process, never any other program. A copy that still has a window is somebody's session — an
/// older build still in use, or an instance that has not been shut down — and is left alone however stubbornly it
/// holds the port.
/// </summary>
public sealed class StaleControllerTests
{
    [Fact]
    public void IsLeftover_TrueForAWindowlessCopyOfThisApp() =>
        Assert.True(StaleController.IsLeftover(StaleController.ProcessName, 4242, hasWindow: false));

    [Fact]
    public void IsLeftover_TrueRegardlessOfTheCaseWindowsReports() =>
        Assert.True(StaleController.IsLeftover(StaleController.ProcessName.ToLowerInvariant(), 4242, hasWindow: false));

    [Fact]
    public void IsLeftover_FalseWhenThatCopyStillHasAWindow() =>
        Assert.False(StaleController.IsLeftover(StaleController.ProcessName, 4242, hasWindow: true));

    [Fact]
    public void IsLeftover_FalseForThisProcess_EvenWithoutAWindow() =>
        Assert.False(StaleController.IsLeftover(StaleController.ProcessName, Environment.ProcessId, hasWindow: false));

    [Theory]
    [InlineData("powershell")]
    [InlineData("dotnet")]
    [InlineData("PoolScoreboard")]
    [InlineData("")]
    [InlineData(null)]
    public void IsLeftover_FalseForAnythingElse(string? processName) =>
        Assert.False(StaleController.IsLeftover(processName, 4242, hasWindow: false));

    [Fact]
    public void Observe_NamesAProcessAndSeesNoWindowInIt()
    {
        using var process = StartWindowlessProcess();
        try
        {
            var observed = StaleController.Observe(process.Id);

            Assert.Equal(process.Id, observed.Pid);
            Assert.Equal("powershell", observed.Name, ignoreCase: true);
            Assert.False(observed.HasWindow);
        }
        finally
        {
            Kill(process);
        }
    }

    [Fact]
    public void Observe_SeesTheWindowAProcessShows()
    {
        // A real window, on a real UI thread: this is the answer that keeps an older build's window safe from being
        // closed, so it is worth reading from Windows rather than reasoning about.
        var (window, dispatcher, thread) = StartWindowThread();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (StaleController.Observe(Environment.ProcessId).HasWindow != true && DateTime.UtcNow < deadline)
                Thread.Sleep(50);

            var observed = StaleController.Observe(Environment.ProcessId);
            Assert.NotNull(observed.Name);
            Assert.True(observed.HasWindow);
        }
        finally
        {
            dispatcher.Invoke(window.Close);
            dispatcher.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void Observe_ReportsNothingForAProcessThatIsGone()
    {
        // "Could not be read" has to stay distinct from "has no window": the first is never acted on.
        var observed = StaleController.Observe(int.MaxValue);

        Assert.Null(observed.Name);
        Assert.Null(observed.HasWindow);
    }

    [Fact]
    public void TryClose_EndsTheProcessItWasPointedAt()
    {
        using var process = StartWindowlessProcess();
        try
        {
            Assert.True(StaleController.TryClose(process.Id, "powershell", TimeSpan.FromSeconds(10)));
            Assert.True(process.WaitForExit(5000));
        }
        finally
        {
            Kill(process);
        }
    }

    [Fact]
    public void TryClose_LeavesAProcessThatIsNotTheExpectedOne()
    {
        // An id the OS recycled between deciding and closing must not take the blame for another process.
        using var process = StartWindowlessProcess();
        try
        {
            Assert.False(StaleController.TryClose(process.Id, StaleController.ProcessName, TimeSpan.FromSeconds(5)));
            Assert.False(process.HasExited);
        }
        finally
        {
            Kill(process);
        }
    }

    [Fact]
    public void TryClose_ReportsFalseForAProcessThatIsNotThere() =>
        Assert.False(StaleController.TryClose(int.MaxValue, StaleController.ProcessName, TimeSpan.FromSeconds(1)));

    private static Process StartWindowlessProcess() =>
        Process.Start(new ProcessStartInfo(
            "powershell",
            "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

    private static void Kill(Process process)
    {
        if (!process.HasExited)
            process.Kill();
    }

    /// <summary>Shows a window from a dedicated STA thread and keeps its dispatcher running until shutdown.</summary>
    private static (Window Window, Dispatcher Dispatcher, Thread Thread) StartWindowThread()
    {
        Dispatcher? dispatcher = null;
        Window? window = null;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            window = new Window { Width = 200, Height = 120, Title = "PoolScoreboard leftover test" };
            window.Show();
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Leftover window test UI thread"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "the test window never appeared");
        return (window!, dispatcher!, thread);
    }
}
