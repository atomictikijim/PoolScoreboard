using System.ComponentModel;
using System.Diagnostics;

namespace PoolScoreboard.Controller.Diagnostics;

/// <summary>
/// Another copy of this app still holding 127.0.0.1:51234 with no window left on screen. A Controller that outlives
/// its window — or one from a build older than the single-instance guard — keeps the port until it is closed, so the
/// overlay can never start and the banner's Retry can never win: the holder has no window for the operator to close.
/// This is what gives the port back — and only in that case; a copy that still shows a window is left alone.
/// <para>
/// Only a process running this app's own executable is ever closed. The running instance holds the session's
/// single-instance mutex, so a <em>live</em> second copy makes the new launch exit before it reaches the port at
/// all; a holder with this name is therefore a leftover. Anything else listening there — another program entirely —
/// is left strictly alone, and the banner goes on naming it.
/// </para>
/// </summary>
internal static class StaleController
{
    /// <summary>The executable name a leftover copy of this app runs under.</summary>
    public const string ProcessName = "PoolScoreboard.Controller";

    /// <summary>
    /// One look at a process: its name, and whether it shows a window — <c>null</c> when the process could not be
    /// read at all (gone, or protected by another integrity level), which is not the same answer as "no window" and
    /// must never be acted on as if it were.
    /// </summary>
    public sealed record Observation(string? Name, bool? HasWindow);

    /// <summary>
    /// Reads a process's name and window state through one handle, so both answers describe the same process at the
    /// same moment; two separate lookups could otherwise straddle the holder exiting and the id being reused.
    /// <para>
    /// Window state is <see cref="Process.MainWindowHandle"/>, which enumerates the process's visible top-level
    /// windows and never sends it a message. That is the right signal here: a minimized Controller keeps its window
    /// visible and still counts as having one, a hung Controller's window still enumerates, and a hidden one (this
    /// app has no tray mode) counts as none. A packaged build would report its exe name here just the same, because
    /// this app is unpackaged and there is no alias to unmask.
    /// </para>
    /// </summary>
    public static Observation Observe(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return new Observation(process.ProcessName, process.MainWindowHandle != IntPtr.Zero);
        }
        catch (Exception exception) when (exception is ArgumentException
                                             or InvalidOperationException
                                             or Win32Exception
                                             or NotSupportedException)
        {
            return new Observation(null, null);
        }
    }

    /// <summary>
    /// Whether the process holding the port is another copy of this app. This process never counts, so a test (or a
    /// listener in the same process) can hold the port without risking being closed.
    /// </summary>
    public static bool IsThisApp(string? processName, int pid) =>
        pid != Environment.ProcessId
        && string.Equals(processName, ProcessName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the port holder is a leftover: another copy of this app that no longer shows a window. A copy that
    /// still has a window is somebody's session — an older build the operator is using, or a second instance that has
    /// not been shut down — and closing it would throw away work in progress, so its port is only reported, never
    /// taken.
    /// </summary>
    public static bool IsLeftover(string? processName, int pid, bool hasWindow) =>
        !hasWindow && IsThisApp(processName, pid);

    /// <summary>
    /// Closes it and waits for it to be gone, so its socket is released before the start is retried. The name is
    /// checked again first, so an id the OS has recycled in the meantime cannot make some other process take the
    /// blame. Never throws: a process that cannot be closed (already gone, or protected) simply leaves the port held,
    /// and the caller falls back to the banner.
    /// </summary>
    public static bool TryClose(int pid, string expectedName, TimeSpan wait)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase))
                return false;

            process.Kill();
            return process.WaitForExit(wait);
        }
        catch (Exception exception) when (exception is ArgumentException
                                             or InvalidOperationException
                                             or Win32Exception
                                             or NotSupportedException)
        {
            return false;
        }
    }
}
