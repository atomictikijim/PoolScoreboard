using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using PoolScoreboard.Controller.Diagnostics;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Persistence;
using PoolScoreboard.Overlay;

namespace PoolScoreboard.Controller;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// The single <see cref="GameManager"/> instance for the app's lifetime, shared between the
    /// WPF UI and the in-process Overlay host so both always read/write the same game state.
    /// </summary>
    public GameManager SharedGameManager { get; } = new();

    private readonly OperatorSettingsStore _settingsStore = new();

    /// <summary>
    /// Coalesces the burst of changes a slider drag or a typed name produces into one file write,
    /// rather than rewriting the settings on every keystroke.
    /// </summary>
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };

    private IHost? _overlayHost;

    /// <summary>
    /// A warning raised during shutdown has no window left to appear in, so everything written to Trace
    /// (including OverlayHostRunner's give-up warnings) also goes to
    /// <c>%AppData%\PoolScoreboard\controller.log</c> for reading after the fact.
    /// </summary>
    private readonly TraceLogFile _traceLog = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PoolScoreboard",
        "controller.log"));

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _traceLog.Attach();

        // From here on a crash explains itself in the log instead of leaving it ending at whatever warning
        // came before. None of these marks the exception handled: the app still fails exactly as it did,
        // the difference is that it now says what failed. The AppDomain handler skips the UI failure it
        // already wrote, since one dispatcher exception reaches both.
        DispatcherUnhandledException += (_, args) =>
            UnhandledExceptionLog.RecordUnhandledUi(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            UnhandledExceptionLog.RecordAppDomainFailure(args.ExceptionObject as Exception, args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            UnhandledExceptionLog.RecordUnobservedTask(args.Exception);

        // Restore before any window exists: GameViewModel reads GameManager's state when it is
        // constructed, so this is what makes both the setup screen and the overlay open with the
        // saved colours, style, visibility and last players instead of the defaults.
        RestoreOperatorProfile();

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveOperatorProfile();
        };

        _overlayHost = OverlayHostRunner.Start(SharedGameManager);
    }

    /// <summary>Asks for the profile to be saved shortly, after the current burst of edits.</summary>
    public void RequestProfileSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Saves the current profile. Never throws: a locked or full disk shouldn't take the app down
    /// mid-stream, and the store logs the failure so a run whose settings went unsaved says so.
    /// </summary>
    public void SaveOperatorProfile()
    {
        _saveTimer.Stop();

        var state = SharedGameManager.GetStateSnapshot();
        _settingsStore.TrySave(
            OperatorProfile.Capture(state),
            state.Player1.EndCapIcon,
            state.Player2.EndCapIcon);
    }

    private void RestoreOperatorProfile()
    {
        var loaded = _settingsStore.Load();
        loaded?.Profile.ApplyTo(SharedGameManager, loaded.HomeIconDataUri, loaded.AwayIconDataUri);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveOperatorProfile();

        try
        {
            // Off the UI thread, or the host's shutdown continuations would be queued to the Dispatcher
            // this method is blocking — see OverlayHostRunner for why that deadlocks the process.
            if (_overlayHost != null)
                OverlayHostRunner.StopAndDispose(_overlayHost, TimeSpan.FromSeconds(2));
        }
        finally
        {
            // Last, so a warning from the stop above still reaches the file (the writer flushes per
            // line), and in a finally so an unexpected throw can't leave the log handle open.
            _traceLog.Dispose();
        }

        base.OnExit(e);
    }
}
