using System.Windows;
using System.Windows.Threading;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The one WPF UI thread these tests use. WPF allows a single <see cref="Application"/> per process, and a window needs
/// one for the brushes and styles the app's markup references, so the Help window and the leftover test's window are
/// both shown here — on the thread that has shown windows on CI without trouble.
/// <para>
/// It used to be one thread per test. The leftover test's own fresh STA thread, with no <see cref="Application"/>,
/// stalled inside <see cref="Window.Show"/> twice on a CI agent, long enough to blow a fifteen-second deadline and
/// leave nothing behind but "no window". Calls here stay bounded for the same reason: a UI thread wedged in
/// <c>Show()</c> fails the test that asked it, instead of holding the run open behind it.
/// </para>
/// </summary>
internal static class WpfTestThread
{
    /// <summary>
    /// The app's own theme, so a window's StaticResource lookups resolve as they do at runtime. Relative on purpose: a
    /// full <c>pack://</c> URI is only parsed once WPF has registered that scheme, and this field is created before any
    /// WPF type has been touched.
    /// </summary>
    private static readonly Uri ThemeUri = new(
        "/PoolScoreboard.Controller;component/Themes/Default.xaml",
        UriKind.Relative);

    private static readonly Lazy<Dispatcher> Thread = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Runs <paramref name="action"/> on the UI thread and waits for it to finish.</summary>
    internal static T Invoke<T>(Func<T> action) => Thread.Value.Invoke(action);

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, but never waits longer than <paramref name="timeout"/>: a UI
    /// thread wedged in <c>Show()</c> must fail the test that asked, not hold the run open behind it.
    /// </summary>
    internal static T InvokeWithin<T>(Func<T> action, TimeSpan timeout)
    {
        var operation = Thread.Value.InvokeAsync(action);
        WaitFor(operation.Task, timeout);
        return operation.Task.Result;
    }

    /// <summary>The same, for work that hands nothing back.</summary>
    internal static void InvokeWithin(Action action, TimeSpan timeout) =>
        WaitFor(Thread.Value.InvokeAsync(action).Task, timeout);

    private static void WaitFor(Task work, TimeSpan timeout)
    {
        if (Task.WhenAny(work, Task.Delay(timeout)).GetAwaiter().GetResult() != work)
            throw new InvalidOperationException(
                $"the WPF test thread did not run its work within {timeout.TotalSeconds:0}s");

        // Rethrows the work's own exception rather than an AggregateException around it.
        work.GetAwaiter().GetResult();
    }

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        Exception? startupFailure = null;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = ThemeUri });
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception exception)
            {
                startupFailure = exception;
            }
            finally
            {
                ready.Set();
            }

            if (startupFailure is null)
                Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "wpf-test-thread",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        if (startupFailure is not null)
            throw new InvalidOperationException("Could not start the WPF thread these tests need.", startupFailure);

        return dispatcher!;
    }
}
