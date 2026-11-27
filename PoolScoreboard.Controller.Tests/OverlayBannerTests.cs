using System.IO;
using System.Windows.Threading;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Controller.ViewModels;
using PoolScoreboard.Core;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The top bar's overlay banner: the reason the session gives is what the window shows, Retry asks the session
/// again and clears the banner when it agrees, and a host going away on another thread reaches the banner through
/// the window's dispatcher. Driven through <see cref="IOverlaySession"/>, so no port is bound and no probe runs.
/// </summary>
public sealed class OverlayBannerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-banner-tests", Guid.NewGuid().ToString("n"));

    private GameViewModel BuildViewModel(GameManager manager, IOverlaySession session, Dispatcher? dispatcher = null) =>
        new(manager, persist: null, overlaySession: session, new PlayerRosterStore(_root), dispatcher, owner: null);

    [Fact]
    public void Banner_IsHidden_WhileTheOverlayIsServing()
    {
        var viewModel = BuildViewModel(new GameManager(), new FakeOverlaySession { IsRunning = true });

        Assert.False(viewModel.OverlayIsOffline);
        Assert.Null(viewModel.OverlayStatus);
    }

    [Fact]
    public void Banner_ShowsTheSessionsReason_AndRetryClearsItOnceTheOverlayServes()
    {
        const string reason = "The overlay is off: 127.0.0.1:51234 is held by powershell (PID 732).";
        var session = new FakeOverlaySession { FailureReason = reason };
        var viewModel = BuildViewModel(new GameManager(), session);

        // A failed start is visible with the session's own words — the window does not re-word them.
        Assert.True(viewModel.OverlayIsOffline);
        Assert.Equal(reason, viewModel.OverlayStatus);

        // Retry goes back to the session rather than deciding anything itself, and the banner follows the reply.
        session.StartSucceeds = true;
        viewModel.RetryOverlayCommand.Execute(null);

        Assert.Equal(1, session.Starts);
        Assert.False(viewModel.OverlayIsOffline);
        Assert.Null(viewModel.OverlayStatus);
    }

    [Fact]
    public void Banner_KeepsTheNewReason_WhenRetryFailsAgain()
    {
        var session = new FakeOverlaySession { FailureReason = "The overlay could not start on 127.0.0.1:51234." };
        var viewModel = BuildViewModel(new GameManager(), session);

        session.FailureReason = "The overlay stopped serving on 127.0.0.1:51234.";
        viewModel.RetryOverlayCommand.Execute(null);

        Assert.Equal(1, session.Starts);
        Assert.True(viewModel.OverlayIsOffline);
        Assert.Equal("The overlay stopped serving on 127.0.0.1:51234.", viewModel.OverlayStatus);
    }

    [Fact]
    public void AStoppedHost_ReachesTheBannerOnTheWindowsDispatcher()
    {
        // The probe reports from a pool thread; the banner is WPF-bound state, so the update has to be posted to
        // the window's dispatcher. A real dispatcher on its own thread is the only way to see that happen.
        var (dispatcher, dispatcherThreadId, thread) = StartDispatcherThread();
        try
        {
            var session = new FakeOverlaySession { IsRunning = true };
            var viewModel = BuildViewModel(new GameManager(), session, dispatcher);

            int? bannerUpdatedOnThread = null;
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GameViewModel.OverlayIsOffline))
                    bannerUpdatedOnThread = Environment.CurrentManagedThreadId;
            };

            session.ReportStopped("The overlay stopped serving on 127.0.0.1:51234.");

            // The update is queued at Normal priority, so this idle-priority call is the barrier that proves it ran.
            dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.True(viewModel.OverlayIsOffline);
            Assert.Equal("The overlay stopped serving on 127.0.0.1:51234.", viewModel.OverlayStatus);
            Assert.Equal(dispatcherThreadId, bannerUpdatedOnThread);
        }
        finally
        {
            dispatcher.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private static (Dispatcher Dispatcher, int ThreadId, Thread Thread) StartDispatcherThread()
    {
        Dispatcher? dispatcher = null;
        var threadId = 0;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            threadId = Environment.CurrentManagedThreadId;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Overlay banner test UI thread"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "the test dispatcher never started");
        return (dispatcher!, threadId, thread);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a passing test over.
        }
    }

    /// <summary>
    /// Stands in for <see cref="OverlayHostSession"/>: a start succeeds or fails on demand, and "the host went away"
    /// can be raised from any thread, which is what the real one does.
    /// </summary>
    private sealed class FakeOverlaySession : IOverlaySession
    {
        public bool IsRunning { get; set; }

        public string? FailureReason { get; set; }

        public bool StartSucceeds { get; set; }

        public int Starts { get; private set; }

        public event EventHandler? Stopped;

        public bool Start(GameManager gameManager)
        {
            Starts++;
            IsRunning = StartSucceeds;
            if (StartSucceeds)
                FailureReason = null;

            return IsRunning;
        }

        public void ReportStopped(string reason)
        {
            IsRunning = false;
            FailureReason = reason;
            Stopped?.Invoke(this, EventArgs.Empty);
        }
    }
}
