using System.Diagnostics;
using System.Windows;
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
        // A real window, on the process's WPF thread: this is the answer that keeps an older build's window safe from
        // being closed, so it is worth reading from Windows rather than reasoning about.
        var window = ShowWindow();
        try
        {
            var observed = WaitForWindow(TimeSpan.FromSeconds(15));

            Assert.NotNull(observed.Name);
            Assert.True(observed.HasWindow);
        }
        finally
        {
            WpfTestThread.InvokeWithin(window.Close, TimeSpan.FromSeconds(5));
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

    /// <summary>
    /// Shows the window on the shared WPF thread. Bounded, so a <c>Show()</c> that wedges fails this assertion instead
    /// of holding the test run open — the way one stalled for more than fifteen seconds on a CI agent when this test
    /// had a fresh STA thread of its own.
    /// </summary>
    private static Window ShowWindow() =>
        WpfTestThread.InvokeWithin(
            () =>
            {
                var window = new Window
                {
                    Width = 200,
                    Height = 120,
                    Title = "PoolScoreboard leftover test",
                    ShowActivated = false,
                };
                window.Show();
                return window;
            },
            TimeSpan.FromSeconds(15));

    /// <summary>
    /// Polls Windows until it reports a window for this process, naming what it saw when it does not: the observation
    /// is the production call this test exists for, so the wait is on it rather than on anything WPF says.
    /// </summary>
    private static StaleController.Observation WaitForWindow(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var observed = StaleController.Observe(Environment.ProcessId);
            if (observed.HasWindow == true)
                return observed;

            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException(
                    $"Windows reported no window for this process {timeout.TotalSeconds:0}s after one was shown " +
                    $"(name: {observed.Name ?? "unreadable"}, window: {observed.HasWindow?.ToString() ?? "unreadable"})");

            Thread.Sleep(50);
        }
    }
}
