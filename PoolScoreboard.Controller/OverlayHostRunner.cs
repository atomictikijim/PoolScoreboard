using Microsoft.Extensions.Hosting;
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

    public static IHost Start(GameManager gameManager) =>
        Task.Run(() => OverlayHost.Start(gameManager)).GetAwaiter().GetResult();

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
            return stopping.Wait(timeout + SettleMargin);
        }
        catch (AggregateException)
        {
            // A failing stop must not abort shutdown; the process can still exit.
            return false;
        }
    }
}
