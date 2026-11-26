using Microsoft.Extensions.Hosting;
using PoolScoreboard.Controller.Diagnostics;
using PoolScoreboard.Core;
using PoolScoreboard.Overlay;

namespace PoolScoreboard.Controller;

/// <summary>
/// Owns the in-process overlay host and the reason it is not running. A failed bind used to be only a log
/// line, so the window kept advertising overlay URLs that answered nothing and there was no way back short of
/// restarting the app. Keeping the failure here is what lets the window show it and offer a Retry once the
/// port is free.
/// </summary>
public sealed class OverlayHostSession
{
    private readonly object _gate = new();
    private readonly int _port;
    private IHost? _host;

    /// <param name="port">The overlay port; a parameter so tests can use a throwaway one.</param>
    public OverlayHostSession(int port = OverlayHost.Port) => _port = port;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _host != null;
        }
    }

    /// <summary>
    /// Why the last start failed, naming the process on the port when it can be identified; null while the
    /// host is running. Safe to read from the UI thread at any time.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>Starts the host if it is not already running. Returns whether it is now serving.</summary>
    public bool Start(GameManager gameManager)
    {
        lock (_gate)
        {
            if (_host != null)
                return true;

            var host = OverlayHostRunner.Start(gameManager, _port);
            if (host == null)
            {
                var owner = PortOwner.Describe(_port);
                FailureReason = owner == null
                    ? $"The overlay could not start on 127.0.0.1:{_port}."
                    : $"The overlay is off: 127.0.0.1:{_port} is held by {owner}.";
                return false;
            }

            _host = host;
            FailureReason = null;
            return true;
        }
    }

    /// <summary>Stops the host if one is running; safe on a session whose start failed.</summary>
    public void Stop(TimeSpan timeout)
    {
        lock (_gate)
        {
            if (_host == null)
                return;

            OverlayHostRunner.StopAndDispose(_host, timeout);
            _host = null;
        }
    }
}
