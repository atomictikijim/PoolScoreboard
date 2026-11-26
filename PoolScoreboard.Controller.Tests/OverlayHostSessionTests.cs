using System.Net;
using System.Net.Sockets;
using PoolScoreboard.Controller;
using PoolScoreboard.Core;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// A failed overlay start has to be visible and recoverable: the window shows the reason (including who holds
/// the port) and Retry starts the host once the port is free. Real listener, real Kestrel, throwaway port.
/// </summary>
public sealed class OverlayHostSessionTests
{
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

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
