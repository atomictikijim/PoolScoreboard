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
    public void HasWindow_FalseForAProcessShowingNothing()
    {
        using var process = Process.Start(new ProcessStartInfo(
            "powershell",
            "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        try
        {
            // A process with no window is exactly what the port may be taken back from.
            Assert.False(StaleController.HasWindow(process.Id));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }

    [Fact]
    public void HasWindow_TrueWhileTheProcessShowsAWindow()
    {
        // A real window, on a real UI thread, so the answer is the one Windows gives — the check that keeps an older
        // build's window safe from being closed.
        var (window, dispatcher, thread) = StartWindowThread();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!StaleController.HasWindow(Environment.ProcessId) && DateTime.UtcNow < deadline)
                Thread.Sleep(50);

            Assert.True(StaleController.HasWindow(Environment.ProcessId));
        }
        finally
        {
            dispatcher.Invoke(window.Close);
            dispatcher.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void TryClose_EndsTheProcessAndWaitsForIt()
    {
        using var process = Process.Start(new ProcessStartInfo(
            "powershell",
            "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        try
        {
            Assert.True(StaleController.TryClose(process.Id, TimeSpan.FromSeconds(10)));
            Assert.True(process.WaitForExit(5000));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }

    [Fact]
    public void TryClose_ReportsFalseForAProcessThatIsNotThere() =>
        Assert.False(StaleController.TryClose(int.MaxValue, TimeSpan.FromSeconds(1)));

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
