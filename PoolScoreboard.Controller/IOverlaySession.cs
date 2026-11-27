using PoolScoreboard.Core;

namespace PoolScoreboard.Controller;

/// <summary>
/// What the window needs from the overlay host: whether it is serving, why it is not, a way to try again, and a
/// warning when a serving host goes away. <see cref="OverlayHostSession"/> is the real implementation; the interface
/// exists so the banner and its Retry button can be driven in a test without binding a port or waiting out the
/// probe.
/// </summary>
public interface IOverlaySession
{
    bool IsRunning { get; }

    /// <summary>
    /// The port the host binds — the one in the URLs the window shows — so the operator can see and change which
    /// one it is when something else holds the usual port.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Points the session at another port, stopping a host that is running so the caller can start it again there.
    /// Changing the port is always a rebind: a listening socket cannot be moved.
    /// </summary>
    void SetPort(int port);

    /// <summary>
    /// Why the last start failed, or why a serving host stopped; null while it is running. The window shows this
    /// text as-is, so naming the port and its owner is the implementation's business.
    /// </summary>
    string? FailureReason { get; }

    /// <summary>Raised when a host that was serving stops without being asked to. May arrive on any thread.</summary>
    event EventHandler? Stopped;

    /// <summary>Starts the host if it is not already running. Returns whether it is now serving.</summary>
    bool Start(GameManager gameManager);
}
