using System.Net;
using System.Net.Sockets;
using PoolScoreboard.Controller;
using PoolScoreboard.Controller.Diagnostics;
using PoolScoreboard.Core;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// A failed overlay start has to be visible and recoverable: the window shows the reason (including who holds
/// the port) and Retry starts the host once the port is free. The same has to hold for a host that stops
/// serving mid-session. Real listener, real Kestrel, throwaway port.
/// </summary>
public sealed class OverlayHostSessionTests
{
    [Fact]
    public void DescribeFailure_AsksForTheWindowToBeClosed_WhenAControllerStillShowsOne()
    {
        var otherPid = Environment.ProcessId + 1;

        // The one case the operator can resolve themselves: another Controller's window is holding the port.
        Assert.Equal(
            $"The overlay is off: 127.0.0.1:51234 is held by {StaleController.ProcessName} (PID {otherPid}). " +
            "Close that Controller window, then choose Retry to take the port back.",
            OverlayHostSession.DescribeFailure(
                51234,
                $"{StaleController.ProcessName} (PID {otherPid})",
                new StaleController.Observation(otherPid, StaleController.ProcessName, HasWindow: true)));
    }

    [Fact]
    public void DescribeFailure_OnlyNamesTheHolder_WhenThereIsNoWindowOfOursToClose()
    {
        var otherPid = Environment.ProcessId + 1;
        var owner = $"{StaleController.ProcessName} (PID {otherPid})";

        // A windowless leftover that would not close: nothing on screen to close, so no advice to give.
        Assert.Equal(
            $"The overlay is off: 127.0.0.1:51234 is held by {owner}.",
            OverlayHostSession.DescribeFailure(
                51234, owner, new StaleController.Observation(otherPid, StaleController.ProcessName, HasWindow: false)));

        // Another program's window is not ours to instruct on — it is named, and that is all.
        Assert.Equal(
            $"The overlay is off: 127.0.0.1:51234 is held by powershell (PID {otherPid}).",
            OverlayHostSession.DescribeFailure(
                51234, $"powershell (PID {otherPid})", new StaleController.Observation(otherPid, "powershell", HasWindow: true)));
    }

    [Fact]
    public void DescribeFailure_SaysThePortCouldNotBeHad_WhenNothingOwnsIt() =>
        Assert.Equal("The overlay could not start on 127.0.0.1:51234.", OverlayHostSession.DescribeFailure(51234, null, null));

    [Fact]
    public void Start_WhenThePortIsTaken_ReportsTheOwner_AndRetrySucceedsOnceItIsFree()
    {
        var port = FreePort();
        var blocker = new TcpListener(IPAddress.Loopback, port);
        blocker.Start();
        var session = new OverlayHostSession(port);

        try
        {
            Assert.False(session.Start(new GameManager()));
            Assert.False(session.IsRunning);
            Assert.Contains($"127.0.0.1:{port}", session.FailureReason);
            Assert.Contains($"PID {Environment.ProcessId}", session.FailureReason);

            blocker.Stop();

            Assert.True(session.Start(new GameManager()));
            Assert.True(session.IsRunning);
            Assert.Null(session.FailureReason);
        }
        finally
        {
            // Always stop the host: a failed assertion above must not leave Kestrel holding the port for the
            // rest of the run.
            session.Stop(TimeSpan.FromSeconds(2));
            blocker.Stop();
        }

        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task HostStoppingMidSession_ReportsThePort_AndRetryServesAgain()
    {
        var port = FreePort();
        var session = new OverlayHostSession(port, TimeSpan.FromMilliseconds(150));
        using var stopped = new ManualResetEventSlim();
        var reports = 0;

        try
        {
            Assert.True(session.Start(new GameManager()));
            session.Stopped += (_, _) =>
            {
                Interlocked.Increment(ref reports);
                stopped.Set();
            };

            // Takes the server down the way a fault in its listener does: no call to Stop, the host just stops
            // answering while the session still believes it is serving.
            Assert.NotNull(session.CurrentHost);
            await session.CurrentHost!.StopAsync();

            Assert.True(stopped.Wait(TimeSpan.FromSeconds(10)), "the session never reported the stopped host");
            Assert.False(session.IsRunning);
            Assert.Contains($"127.0.0.1:{port}", session.FailureReason);

            // Retry, which is what the banner's button does: the stopped host released the port, so the
            // overlay comes back without a restart.
            Assert.True(session.Start(new GameManager()));
            Assert.True(session.IsRunning);
            Assert.Null(session.FailureReason);

            // One banner per failure: rounds that were already in flight when it fired must not report again.
            Thread.Sleep(500);
            Assert.Equal(1, reports);
        }
        finally
        {
            session.Stop(TimeSpan.FromSeconds(2));
        }

        Assert.False(session.IsRunning);
    }

    [Fact]
    public void StopWhileServing_IsNotReportedAsAFailure()
    {
        var session = new OverlayHostSession(FreePort(), TimeSpan.FromMilliseconds(100));
        var reports = 0;
        session.Stopped += (_, _) => Interlocked.Increment(ref reports);

        Assert.True(session.Start(new GameManager()));

        // Long enough for several probes to come back fine: a serving overlay must never raise the banner.
        Thread.Sleep(600);
        Assert.True(session.IsRunning);
        Assert.Null(session.FailureReason);

        session.Stop(TimeSpan.FromSeconds(5));

        // The shutdown runs the same probe path; a stop the app asked for must not leave a banner behind or
        // point the operator at a Retry it did not need.
        Assert.Equal(0, reports);
        Assert.False(session.IsRunning);
        Assert.Null(session.FailureReason);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
