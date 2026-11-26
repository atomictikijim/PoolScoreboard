using System.Net;
using System.Net.Sockets;
using PoolScoreboard.Controller.Diagnostics;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The overlay's failure message has to name the process holding the port, which means asking Windows for the
/// listener's owning pid. Binding a listener in this process is the only owner a test can guarantee.
/// </summary>
public sealed class PortOwnerTests
{
    [Fact]
    public void FindOwningProcessId_NamesTheProcessListeningOnThePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.Equal(Environment.ProcessId, PortOwner.FindOwningProcessId(port));
        Assert.Contains($"PID {Environment.ProcessId}", PortOwner.Describe(port));
    }
}
