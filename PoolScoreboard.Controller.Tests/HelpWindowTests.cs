using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Reads the Help page the way an operator reads it off the screen, without needing an operator: the window is
/// built at a non-default port on an STA thread against the same theme dictionary the app merges, shown off the
/// visible desktop so its bindings resolve, and its text read back through the logical tree. This is what
/// <c>tools/verify-live.ps1 -Scenario port-change</c> used to be the only check for — the page hard-coding an
/// address again fails here, on every push, instead of only when someone runs the live check on another port.
/// </summary>
public sealed class HelpWindowTests
{
    private const string BaseUrl = "http://127.0.0.1:51241";

    /// <summary>
    /// The app's own theme, so the window's StaticResource lookups resolve as they do at runtime. Relative on
    /// purpose: a full <c>pack://</c> URI is only parsed once WPF has registered that scheme, and this field is
    /// created before any WPF type has been touched.
    /// </summary>
    private static readonly Uri ThemeUri = new(
        "/PoolScoreboard.Controller;component/Themes/Default.xaml",
        UriKind.Relative);

    [Fact]
    public void EveryUrlLineShowsThePortItWasBuiltWith()
    {
        var texts = OnHelpThread(() =>
        {
            var window = new HelpWindow(BaseUrl)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowActivated = false,
                ShowInTaskbar = false,
                Left = -20000,
                Top = -20000
            };

            try
            {
                // The URL lines are bindings, and a binding resolves when its tree is loaded — which for a window
                // means it needs a handle. Showing it well off the visible desktop is the cheapest way to get
                // that inside a test; nothing here asserts anything about layout.
                window.Show();
                window.UpdateLayout();
                return TextBlocks(window).Select(block => block.Text).ToArray();
            }
            finally
            {
                window.Close();
            }
        });

        var urls = texts.Where(text => text.Contains("http://", StringComparison.Ordinal)).ToArray();

        // Every overlay page and Stream Deck endpoint the page lists. A line that silently disappears is a
        // regression too, so the count is pinned rather than merely "at least one".
        Assert.Equal(13, urls.Length);
        Assert.All(urls, url => Assert.Contains(BaseUrl + "/", url, StringComparison.Ordinal));
        Assert.DoesNotContain(urls, url => url.Contains("51234", StringComparison.Ordinal));
    }

    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node)
                continue;

            if (node is TextBlock block)
                yield return block;

            foreach (var nested in TextBlocks(node))
                yield return nested;
        }
    }

    private static T OnHelpThread<T>(Func<T> action) => HelpThread.Value.Invoke(action);

    /// <summary>
    /// A window needs an STA thread and an <see cref="Application"/>, which is what supplies BackgroundBrush and
    /// the button styles it references. One thread and one Application serve every test in this class: WPF allows
    /// only one Application per process, and a brush made on one thread cannot be used from another.
    /// </summary>
    private static readonly Lazy<Dispatcher> HelpThread =
        new(StartHelpThread, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Dispatcher StartHelpThread()
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
            Name = "help-window-tests"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        if (startupFailure is not null)
            throw new InvalidOperationException("Could not start the WPF thread the Help window needs.", startupFailure);

        return dispatcher!;
    }
}
