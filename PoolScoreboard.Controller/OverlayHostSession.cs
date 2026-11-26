using System.Diagnostics;
using System.Net.Http;
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
/// <para>
/// A host that started can still stop serving mid-session — a fault in its listener, or a request loop that no
/// longer answers — and nothing in the app notices, because the host is started rather than run and its
/// stopping token is never observed. So a probe of the state endpoint runs while a host is up: an overlay that
/// stops answering comes back as the same banner and Retry, in the middle of a match instead of at the next
/// restart.
/// </para>
/// </summary>
public sealed class OverlayHostSession
{
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(3);

    /// <summary>How long one probe may take before the overlay counts as not answering.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Pause before a missing answer is checked again. One miss can be a busy moment, and the banner should
    /// mean the overlay is gone rather than that it was slow once.
    /// </summary>
    private static readonly TimeSpan ConfirmDelay = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly int _port;
    private readonly TimeSpan _checkInterval;
    private readonly HttpClient _probe = new() { Timeout = CheckTimeout };

    /// <summary>Ticks only while a host is serving; see <see cref="Check"/>.</summary>
    private readonly Timer _watch;

    private IHost? _host;

    /// <summary>1 while a probe round is running; see <see cref="Check"/>.</summary>
    private int _checking;

    /// <param name="port">The overlay port; a parameter so tests can use a throwaway one.</param>
    /// <param name="checkInterval">How often the running overlay is probed; tests use a short one.</param>
    public OverlayHostSession(int port = OverlayHost.Port, TimeSpan? checkInterval = null)
    {
        _port = port;
        _checkInterval = checkInterval ?? DefaultCheckInterval;
        _watch = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _host != null;
        }
    }

    /// <summary>
    /// Why the last start failed, naming the process on the port when it can be identified, or why a serving
    /// host stopped; null while the host is running. Safe to read from the UI thread at any time.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Raised when a host that was serving stops answering, after <see cref="FailureReason"/> says which port
    /// went away and the dead host has been released. The handler runs on a pool thread, so the window hops to
    /// its own thread before showing it.
    /// </summary>
    public event EventHandler? Stopped;

    /// <summary>The running host; for tests that need to make it stop serving the way a fault does.</summary>
    internal IHost? CurrentHost
    {
        get
        {
            lock (_gate)
                return _host;
        }
    }

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

            // Also what makes a Retry watched: the timer is per session, and the identity check in
            // ReportStopped is what keeps an older host's slow answer from blaming the new one.
            _watch.Change(_checkInterval, _checkInterval);
            return true;
        }
    }

    /// <summary>Stops the host if one is running; safe on a session whose start failed.</summary>
    public void Stop(TimeSpan timeout)
    {
        lock (_gate)
        {
            // Stopped before the host is, so a probe already in flight can't report the shutdown as a failure;
            // one that is already running lands in ReportStopped's identity check, which no longer matches.
            _watch.Change(Timeout.Infinite, Timeout.Infinite);

            if (_host == null)
                return;

            OverlayHostRunner.StopAndDispose(_host, timeout);
            _host = null;
        }
    }

    private void Check()
    {
        // A round can outlast the interval — two timeouts plus the confirming pause — and stacked rounds would
        // only pile more requests on a host already suspected of not answering, then race to report it.
        if (Interlocked.Exchange(ref _checking, 1) == 1)
            return;

        try
        {
            IHost? host;
            lock (_gate)
                host = _host;

            if (host == null || Serves() || ServesAfterPause())
                return;

            ReportStopped(host);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    /// <summary>One probe of the endpoint the overlay itself reads state from.</summary>
    private bool Serves()
    {
        try
        {
            using var response = _probe
                .GetAsync($"http://127.0.0.1:{_port}/overlay/api/scoreboard/state")
                .GetAwaiter()
                .GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                             or TaskCanceledException
                                             or InvalidOperationException)
        {
            // Refused, timed out or the client is unusable — all mean it is not serving right now.
            return false;
        }
    }

    private bool ServesAfterPause()
    {
        Thread.Sleep(ConfirmDelay);
        return Serves();
    }

    private void ReportStopped(IHost host)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_host, host))
                return;

            _host = null;
            _watch.Change(Timeout.Infinite, Timeout.Infinite);
            FailureReason = $"The overlay stopped serving on 127.0.0.1:{_port}.";
        }

        Trace.TraceError(
            $"Overlay host stopped serving 127.0.0.1:{_port} mid-session; the overlay and Stream Deck " +
            "control are offline until it is started again.");

        // Released before the banner appears, so the Retry the operator reaches for finds the port free. The
        // server is already down, which is why this is only cleanup — the timeout covers a wedged stop.
        OverlayHostRunner.StopAndDispose(host, TimeSpan.FromSeconds(2));

        Stopped?.Invoke(this, EventArgs.Empty);
    }
}
