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
    /// A window shown from its own STA thread, keeping apart the two things the test needs: that the thread is up,
    /// and that Windows reports the window. They used to be one handshake, and the handshake completed only once
    /// <see cref="Window.Show"/> had returned — but showing the process's first window is the slow and fragile part
    /// (it loads the theme, creates an HWND and, with the default <c>ShowActivated</c>, asks the session for the
    /// foreground). On a CI agent that request can stall, and a stall or a throw both looked the same from the test
    /// thread: "the test window never appeared", ten seconds later, with no clue which one it was.
    /// </summary>
    private sealed class TestWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly Dispatcher _dispatcher;
        private readonly Task _shown;
        private Window? _window;

        private TestWindow(Thread thread, Dispatcher dispatcher, Task shown, Window? window)
        {
            _thread = thread;
            _dispatcher = dispatcher;
            _shown = shown;
            _window = window;
        }

        /// <summary>
        /// Starts the thread and returns once it is running — milliseconds, and not what this test asserts. The
        /// window itself is created and shown afterwards, so a test that fails is told which of the two waited.
        /// </summary>
        internal static TestWindow Start()
        {
            Window? window = null;
            Dispatcher? dispatcher = null;
            var up = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                up.TrySetResult();

                try
                {
                    // No activation: the foreground request ShowActivated makes goes through the session's input
                    // queue, which a test agent does not own — the Help window test shows its window the same way.
                    // Stay in the task bar on purpose: ShowInTaskbar = false gives the window an owner, and
                    // Process.MainWindowHandle only considers a process's unowned visible windows.
                    window = new Window
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

            if (!up.Task.Wait(TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException(
                    $"the window thread did not start within 10s (thread state: {thread.ThreadState})");

            return new TestWindow(thread, dispatcher!, shown.Task, window);
        }

        /// <summary>
        /// Waits for Windows to report the window this thread showed — the question the test actually asks. A
        /// <c>Show()</c> that threw surfaces as itself; one that has not returned is named in the timeout, so a
        /// second flake says which stage stalled instead of only that nothing appeared.
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

                // A thread that ended without the window becoming visible is a different failure from a window
                // that is merely slow, and neither is worth the rest of the deadline.
                if (!_thread.IsAlive)
                    throw new InvalidOperationException(
                        "the window thread ended before Windows reported a window " +
                        $"(thread state: {_thread.ThreadState})");

                if (DateTime.UtcNow >= deadline)
                    throw new InvalidOperationException(
                        $"Windows reported no window {timeout.TotalSeconds:0}s after the window thread started " +
                        $"(Show() returned: {(_shown.IsCompleted ? "yes" : "no")}, thread state: {_thread.ThreadState})");

                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Closes the window on its own thread and ends the dispatcher. Never waits behind a <c>Show()</c> that has
        /// not returned: a test that already failed must not also hang in cleanup. The thread is background, so a
        /// stuck one cannot keep the run alive either.
        /// </summary>
        public void Dispose()
        {
            if (_thread.IsAlive && !_dispatcher.HasShutdownStarted)
            {
                try
                {
                    _dispatcher.InvokeAsync(() => _window?.Close(), DispatcherPriority.Send)
                        .Task.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                    // Already closed; joining the thread below is all that is left to do.
                }

                _dispatcher.InvokeShutdown();
            }

            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
