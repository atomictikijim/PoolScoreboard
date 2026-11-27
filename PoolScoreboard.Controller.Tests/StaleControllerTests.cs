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
        using var shown = TestWindow.Start();

        var observed = shown.WaitUntilVisible(TimeSpan.FromSeconds(15));

        Assert.NotNull(observed.Name);
        Assert.True(observed.HasWindow);
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
    /// A window shown from its own STA thread. Nothing waits for the thread to start: showing a window is the slow
    /// and fragile half — the process's first window loads its theme, creates an HWND and, with the default
    /// <c>ShowActivated</c>, asks the session for the foreground, which a CI agent's input queue can withhold — so
    /// what the test waits for is Windows reporting it. It used to be one handshake that completed only once
    /// <see cref="Window.Show"/> had returned, which made a stalled <c>Show()</c>, a thrown one and a thread that
    /// never started all read as "the test window never appeared" ten seconds later.
    /// </summary>
    private sealed class TestWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly Task _shown;

        private TestWindow(Thread thread, Task shown)
        {
            _thread = thread;
            _shown = shown;
        }

        internal static TestWindow Start()
        {
            var shown = new TaskCompletionSource();

            var thread = new Thread(() =>
            {
                try
                {
                    // No activation: the foreground request ShowActivated makes goes through the session's input
                    // queue, which a test agent does not own — the Help window test shows its window the same way.
                    // Stay in the task bar on purpose: ShowInTaskbar = false gives the window an owner, and
                    // Process.MainWindowHandle only considers a process's unowned visible windows.
                    var window = new Window
                    {
                        Width = 200,
                        Height = 120,
                        Title = "PoolScoreboard leftover test",
                        ShowActivated = false,
                    };
                    window.Show();
                    shown.TrySetResult();
                }
                catch (Exception exception)
                {
                    // Carried back with its own stack: a window that could not be shown must not read as one that
                    // never appeared.
                    shown.TrySetException(exception);
                    return;
                }

                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "Leftover window test UI thread",
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return new TestWindow(thread, shown.Task);
        }

        /// <summary>
        /// Waits for Windows to report the window this thread showed — the question the test actually asks. A
        /// <c>Show()</c> that threw surfaces as itself; one that has not returned is named in the timeout, with the
        /// thread's own state, so a repeat says whether it is stuck in <c>Show()</c> or gone.
        /// </summary>
        internal StaleController.Observation WaitUntilVisible(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                if (_shown.IsCompleted)
                {
                    _shown.GetAwaiter().GetResult();

                    var visible = StaleController.Observe(Environment.ProcessId);
                    if (visible.HasWindow == true)
                        return visible;
                }

                if (DateTime.UtcNow >= deadline)
                    throw new InvalidOperationException(
                        $"Windows reported no window {timeout.TotalSeconds:0}s after the window thread started " +
                        $"(Show() returned: {(_shown.IsCompleted ? "yes" : "no")}, thread state: {_thread.ThreadState})");

                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Ends <c>Dispatcher.Run</c> — or the <c>Run</c> still to come, if <c>Show()</c> has not returned — and joins
        /// the thread, which takes its window with it. Nothing is queued onto the dispatcher and nothing waits on it,
        /// so a stuck <c>Show()</c> cannot turn a failed assertion into a hang in cleanup.
        /// </summary>
        public void Dispose()
        {
            if (Dispatcher.FromThread(_thread) is { } dispatcher && !dispatcher.HasShutdownStarted)
                dispatcher.InvokeShutdown();

            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
