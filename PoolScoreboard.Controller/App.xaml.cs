using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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

    private readonly MatchStateStore _matchStore = new();

    /// <summary>
    /// Coalesces the burst of changes a slider drag or a typed name produces into one file write,
    /// rather than rewriting the settings on every keystroke.
    /// </summary>
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };

    /// <summary>
    /// Keeps <c>match.json</c> in step with the match in progress — debounced, deleted when the state is back at
    /// setup, and restored before the window exists. Its timer and rules live in there rather than here so they can
    /// be tested without an app around them.
    /// </summary>
    private readonly MatchAutosave _matchAutosave;

    /// <summary>
    /// The finished-match history and the thing that files into it. The rule — one record per match, keyed on the
    /// match's own id — lives in there, so it can be tested without an app around it.
    /// </summary>
    private readonly MatchHistoryStore _matchHistoryStore = new();

    private readonly MatchHistoryRecorder _matchHistoryRecorder;

    /// <summary>
    /// Applies the saved port before anything binds and records the port in use when the profile is saved.
    /// Its rules live in there rather than here so both directions can be tested without a window.
    /// </summary>
    private readonly OverlayPortSettings _overlayPortSettings;

    public App()
    {
        _matchAutosave = new MatchAutosave(SharedGameManager, _matchStore);
        _matchHistoryRecorder = new MatchHistoryRecorder(SharedGameManager, _matchHistoryStore);
        _overlayPortSettings = new OverlayPortSettings(OverlaySession);
    }

    /// <summary>
    /// The overlay host and, when it could not bind, the reason the window shows next to a Retry button.
    /// </summary>
    public OverlayHostSession OverlaySession { get; } = new();

    /// <summary>
    /// Non-null only while this process is the one running Controller. A duplicate launch leaves it null,
    /// asks the running window to come forward and exits before touching any saved state.
    /// </summary>
    private SingleInstanceGuard? _instanceGuard;

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

        // Attached before anything reads settings or binds the overlay port: a second copy would share
        // %AppData% with the first (last save wins) and could never host the overlay on 51234 anyway.
        _instanceGuard = SingleInstanceGuard.TryAcquire(
            SingleInstanceGuard.DefaultMutexName,
            SingleInstanceGuard.DefaultActivationName,
            FocusRunningWindow);

        if (_instanceGuard == null)
        {
            // The running instance owns the profile, so this launch must not restore or save anything; say
            // why it is going away and leave.
            Trace.TraceWarning(
                "Another PoolScoreboard Controller is already running; it was brought to the front and this " +
                "launch will exit.");
            Shutdown(0);
            return;
        }

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

        // After the profile — it supplies the players and Race-To targets the scores are judged against — and
        // before the window, which reads state when it is constructed.
        _matchAutosave.Restore();

        // After the restore, so a match that was already won before the crash is not filed again; from here every
        // finished match — played in the window, on the keyboard or through the Stream Deck — is recorded once.
        _matchHistoryRecorder.Start();

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveOperatorProfile();
        };

        // Restore() also started the autosave watching, so every mutation from here on — the UI, the keyboard, the
        // Stream Deck's HTTP calls — schedules a snapshot without App doing anything.
        OverlaySession.Start(SharedGameManager);
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
        var profile = OperatorProfile.Capture(state);

        // Not in the snapshot: the port belongs to the host, and the session is what knows which one is in use.
        _overlayPortSettings.RecordPortInUse(profile);

        _settingsStore.TrySave(profile, state.Player1.EndCapIcon, state.Player2.EndCapIcon);
    }

    /// <summary>
    /// Runs on a thread-pool thread when another launch asks for the window. Activate alone does not always
    /// raise a window that is already behind another app, so the topmost flip forces it forward without
    /// leaving it pinned.
    /// </summary>
    private void FocusRunningWindow()
    {
        var dispatcher = Dispatcher;

        // A second launch can arrive while this instance is closing. This runs on a pool thread, so posting
        // to a dispatcher that has already begun shutting down would throw there and take the process down
        // with an error instead of letting it exit cleanly.
        if (dispatcher.HasShutdownStarted)
            return;

        try
        {
            dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is not { IsLoaded: true } window)
                    return;

                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;

                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
            });
        }
        catch (InvalidOperationException)
        {
            // Shutdown began between the check and the post; the signal is simply dropped.
        }
    }

    private void RestoreOperatorProfile()
    {
        var loaded = _settingsStore.Load();
        if (loaded == null)
            return;

        // The overlay may be on another port than this app's default: settings.json is where the operator's
        // choice lives, and the session has to know it before anything tries to bind.
        _overlayPortSettings.ApplySavedPort(loaded.Profile);
        loaded.Profile.ApplyTo(SharedGameManager, loaded.HomeIconDataUri, loaded.AwayIconDataUri);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var guard = _instanceGuard;
        if (guard == null)
        {
            // A duplicate launch never restored the profile, so saving here would replace the running
            // instance's settings with defaults. It has no host to stop either.
            _traceLog.Dispose();
            base.OnExit(e);
            return;
        }

        SaveOperatorProfile();

        // The debounced snapshot may not have run yet, and the last point scored is exactly what has to
        // survive a crash or a forced restart.
        _matchAutosave.SaveNow();

        try
        {
            // Off the UI thread, or the host's shutdown continuations would be queued to the Dispatcher
            // this method is blocking — see OverlayHostRunner for why that deadlocks the process.
            OverlaySession.Stop(TimeSpan.FromSeconds(2));
        }
        finally
        {
            // Log last, so a warning from the stop above still reaches the file (the writer flushes per
            // line), and in a finally so an unexpected throw can't leave the log handle open.
            _matchHistoryRecorder.Dispose();
            _matchAutosave.Dispose();
            guard.Dispose();
            _traceLog.Dispose();
        }

        base.OnExit(e);
    }
}
