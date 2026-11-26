using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using PoolScoreboard.Controller.Diagnostics;
using PoolScoreboard.Core;
using PoolScoreboard.Overlay;

namespace PoolScoreboard.Controller;

/// <summary>
/// Starts and stops the in-process overlay host off the WPF UI thread.
/// <para>
/// ASP.NET Core's start/stop path awaits without <c>ConfigureAwait(false)</c> in places, so it captures
/// the <see cref="SynchronizationContext"/> of whichever thread calls it. On the WPF UI thread that
/// context posts continuations back to the Dispatcher — and <c>App.OnExit</c> blocks that same thread
/// waiting for the stop, so those continuations can never run. The result is a deadlock that no timeout
/// can break, because the timeout's own continuation is queued behind it: the server has already stopped
/// answering, `OnExit` never returns, and the process lingers invisibly until killed.
/// </para>
/// <para>
/// Running both calls on the thread pool means there is no UI context to capture, so shutdown completes
/// normally. This is the whole reason this type exists rather than calling the host directly.
/// </para>
/// </summary>
internal static class OverlayHostRunner
{
    /// <summary>How long past the requested timeout to wait for the stop task to finish.</summary>
    private static readonly TimeSpan SettleMargin = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Starts the overlay host, returning null when it could not start — almost always because something else
    /// owns 127.0.0.1:51234, such as a leftover Controller process. A failed bind used to throw out of
    /// <c>OnStartup</c> and take the whole app down before any window appeared, with nothing in the log; the
    /// scoreboard is still usable by hand, so startup continues and the reason goes to the log instead.
    /// </summary>
    public static IHost? Start(GameManager gameManager, int port = OverlayHost.Port)
    {
        try
        {
            return Task.Run(() => OverlayHost.Start(gameManager, port)).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception.GetBaseException() is IOException or SocketException)
        {
            var owner = PortOwner.Describe(port);
            Trace.TraceError(
                $"Overlay host could not start on 127.0.0.1:{port}: {exception.GetBaseException().Message}" +
                (owner == null ? string.Empty : $" The port is held by {owner}.") +
                " The overlay and Stream Deck control will not work until the Controller is restarted with that " +
                "port free.");
            return null;
        }
    }

    /// <summary>
    /// Stops and disposes the host, waiting no longer than <paramref name="timeout"/> plus a settle
    /// margin. Returns false when it could not be stopped in time, in which case the abandoned stop is
    /// left running on a pool thread — which never keeps the process alive — instead of hanging shutdown.
    /// </summary>
    public static bool StopAndDispose(IHost host, TimeSpan timeout)
    {
        var stopping = Task.Run(() =>
        {
            host.StopAsync(timeout).GetAwaiter().GetResult();
            host.Dispose();
        });

        try
        {
            // Wait rather than awaiting: the caller is the UI thread and must not yield to it here.
            if (stopping.Wait(timeout + SettleMargin))
                return true;

            // Shutdown is about to exit without the server having stopped cleanly; without this the
            // operator would never know, since there is no window left to show it in.
            Trace.TraceWarning(
                $"Overlay host did not stop within {timeout.TotalSeconds:0.#}s; shutting down anyway.");
            return false;
        }
        catch (AggregateException exception)
        {
            // A failing stop must not abort shutdown; the process can still exit.
            Trace.TraceWarning($"Overlay host failed to stop: {exception.GetBaseException().Message}");
            return false;
        }
    }
}
