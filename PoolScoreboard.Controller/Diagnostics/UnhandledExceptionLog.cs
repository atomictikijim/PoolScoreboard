using System.Diagnostics;
using System.Text;

namespace PoolScoreboard.Controller.Diagnostics;

/// <summary>
/// Records an exception that reached the very top of the app — the WPF dispatcher, the AppDomain, or a
/// task nobody awaited — as an error line in <see cref="Trace"/>, which <see cref="TraceLogFile"/> writes
/// to <c>controller.log</c>. Without it a crash left a log that simply stopped at the last warning, with
/// nothing saying why the window disappeared.
/// </summary>
internal static class UnhandledExceptionLog
{
    /// <summary>
    /// The exception the UI thread's last-chance handler already recorded. WPF does not stop an unhandled
    /// dispatcher exception from reaching the CLR, so <see cref="AppDomain.UnhandledException"/> fires for
    /// that same object moments later; remembering it keeps one crash from filling the log with two copies
    /// of the same stack.
    /// </summary>
    private static Exception? _uiFailure;

    /// <summary>Records an exception that escaped the UI thread, and remembers it for the check below.</summary>
    public static void RecordUnhandledUi(Exception exception)
    {
        _uiFailure = exception;
        Record("Unhandled UI exception", exception);
    }

    /// <summary>
    /// Records an exception that escaped any thread. The one the UI handler already wrote is skipped: the
    /// process is still ending, and the dispatcher's entry says more than "terminating" does.
    /// </summary>
    public static void RecordAppDomainFailure(Exception? exception, bool terminating)
    {
        if (exception != null && ReferenceEquals(exception, _uiFailure))
            return;

        Record(terminating ? "Unhandled exception, process is terminating" : "Unhandled exception", exception);
    }

    /// <summary>Records an exception from a task nobody awaited; on .NET 8 this does not end the process.</summary>
    public static void RecordUnobservedTask(Exception exception) =>
        Record("Unobserved task exception", exception);

    /// <summary>
    /// Writes one entry: what was running when it failed, the exception's type and message, its stack
    /// trace and any inner exceptions. Every caller is on a path the process may not return from, so this
    /// only formats and hands the text to Trace — the file listener appends each line as it is written, so
    /// the entry is already on disk by the time the process goes away.
    /// </summary>
    private static void Record(string source, Exception? exception)
    {
        var entry = new StringBuilder(source);
        entry.Append(": ");

        if (exception == null)
        {
            entry.Append("no exception details were provided.");
        }
        else
        {
            entry.Append(exception.GetType().FullName).Append(": ").Append(exception.Message);

            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
                entry.AppendLine().Append(exception.StackTrace.TrimEnd());

            for (var inner = exception.InnerException; inner != null; inner = inner.InnerException)
            {
                entry.AppendLine()
                    .Append("   ---> ")
                    .Append(inner.GetType().FullName)
                    .Append(": ")
                    .Append(inner.Message);
            }
        }

        Trace.TraceError(entry.ToString());
    }
}
