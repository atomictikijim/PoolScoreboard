using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

    public void SaveOperatorProfile()
    {
        _saveTimer.Stop();

        try
        {
            var state = SharedGameManager.GetStateSnapshot();
            _settingsStore.Save(
                OperatorProfile.Capture(state),
                state.Player1.EndCapIcon,
                state.Player2.EndCapIcon);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked or full disk shouldn't take the app down mid-stream; the next edit retries and
            // the profile simply doesn't persist this run.
        }
    }

    private void RestoreOperatorProfile()
    {
        var loaded = _settingsStore.Load();
        loaded?.Profile.ApplyTo(SharedGameManager, loaded.HomeIconDataUri, loaded.AwayIconDataUri);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveOperatorProfile();

        // Off the UI thread, or the host's shutdown continuations would be queued to the Dispatcher this
        // method is blocking — see OverlayHostRunner for why that deadlocks the process.
        if (_overlayHost != null)
            OverlayHostRunner.StopAndDispose(_overlayHost, TimeSpan.FromSeconds(2));

        base.OnExit(e);
    }
}
