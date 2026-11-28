using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.ViewModels;

public partial class GameViewModel : ObservableObject
{
    private static readonly Dictionary<int, string> BaseBallColorHex = new()
    {
        [1] = "#F2C200",
        [2] = "#1D4E9B",
        [3] = "#C0392B",
        [4] = "#6C3483",
        [5] = "#E07B27",
        [6] = "#1E8449",
        [7] = "#7B241C",
        [8] = "#111111"
    };

    private readonly GameManager _gameManager;
    private readonly Action? _persist;
    private readonly IOverlaySession? _overlaySession;
    private readonly PlayerRosterStore _rosterStore;

    /// <summary>
    /// The recorded matches: what the save button files into and what the History window reads. A path, not a
    /// cache, so handing the same folder a second instance reads what the first wrote.
    /// </summary>
    private readonly MatchHistoryStore _historyStore;

    /// <summary>
    /// The match whose record has already been looked for in the history, so a won match costs one file read — at
    /// the moment it is won — rather than one per state change while its banner is up.
    /// </summary>
    private string? _historyCheckedMatchId;

    /// <summary>
    /// Where game-state events are marshalled to. Supplied rather than read from
    /// <see cref="Application.Current"/>: a test has no running app and wants the handlers to run inline, which
    /// is what null means here.
    /// </summary>
    private readonly Dispatcher? _dispatcher;

    /// <summary>Owner for the dialogs this view model opens; null when there is no window (tests).</summary>
    private readonly Window? _owner;
    private readonly PlayerRoster _roster;

    /// <summary>The saved players both sides' pickers bind to, most recently used first.</summary>
    public ObservableCollection<SavedPlayerOption> SavedPlayers { get; } = new();

    /// <summary>
    /// True while the constructor copies the restored profile into the properties below. The change
    /// handlers are suppressed for that window: those values came out of GameManager, so pushing them
    /// back would be a burst of no-op mutations (and would persist a file for each one).
    /// </summary>
    private bool _restoring;
    private readonly Dictionary<int, BallItemViewModel> _allBalls = new();

    // --- Match setup (editable before the match starts) ---

    [ObservableProperty]
    private GameType selectedGameType = GameType.NineBall;

    [ObservableProperty]
    private RaceToMode selectedRaceToMode = RaceToMode.Single;

    /// <summary>
    /// How long a shot gets, chosen on this screen and carried into the match it starts. Clamped by
    /// <see cref="GameManager.SetShotClockLengths" />, so a slipped digit leaves a usable clock.
    /// </summary>
    [ObservableProperty]
    private int shotClockSeconds = GameState.DefaultShotClockSeconds;

    /// <summary>What calling an extension adds — the whole of the bonus, since a player gets one per rack.</summary>
    [ObservableProperty]
    private int shotClockExtensionSeconds = GameState.DefaultShotClockExtensionSeconds;

    [ObservableProperty]
    private PlayerViewModel? homePlayer;

    [ObservableProperty]
    private PlayerViewModel? awayPlayer;

    [ObservableProperty]
    private string homeBackgroundColor = "#1a2332";

    [ObservableProperty]
    private string awayBackgroundColor = "#1a2332";

    [ObservableProperty]
    private string homeAccentColor = "#00d4ff";

    [ObservableProperty]
    private string awayAccentColor = "#00d4ff";

    [ObservableProperty]
    private string homeTextColor = "#f0f0f0";

    [ObservableProperty]
    private string awayTextColor = "#f0f0f0";

    [ObservableProperty]
    private int cornerRoundness = 100;

    [ObservableProperty]
    private int overallScale = 100;

    [ObservableProperty]
    private bool glossyFinish = true;

    [ObservableProperty]
    private EndCapStyle selectedEndCapStyle = EndCapStyle.Dot;

    [ObservableProperty]
    private ShooterIndicatorStyle selectedShooterIndicatorStyle = ShooterIndicatorStyle.Glow;

    [ObservableProperty]
    private bool gameInitialized = false;

    // --- Live game state (mirrored from GameManager once the match starts) ---

    [ObservableProperty]
    private int homeScore;

    [ObservableProperty]
    private int awayScore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentShooterButtonText))]
    private bool homeIsCurrentShooter = true;

    public string CurrentShooterButtonText => HomeIsCurrentShooter ? "HOME Shooting" : "AWAY Shooting";

    [ObservableProperty]
    private BallGroup homeBallGroup = BallGroup.Unassigned;

    [ObservableProperty]
    private BallGroup awayBallGroup = BallGroup.Unassigned;

    [ObservableProperty]
    private IReadOnlyList<BallItemViewModel> numericRowBalls = Array.Empty<BallItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<BallItemViewModel> homeGroupBalls = Array.Empty<BallItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<BallItemViewModel> awayGroupBalls = Array.Empty<BallItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<BallItemViewModel> eightBallItems = Array.Empty<BallItemViewModel>();

    [ObservableProperty]
    private string? winnerAnnouncement;

    [ObservableProperty]
    private bool scoreBarVisible = true;

    [ObservableProperty]
    private bool ballTrackerVisible = true;

    [ObservableProperty]
    private bool winnerBannerVisible = true;

    /// <summary>
    /// Where the overlay is served, so the operator can move it when another program already holds the usual port
    /// instead of losing the overlay for the session. 1024-65535 only; a value outside that snaps back to the one
    /// the host is actually on.
    /// </summary>
    [ObservableProperty]
    private int overlayPort = OperatorProfile.DefaultOverlayPort;

    /// <summary>The base address the Help window lists, so it never names a port the app is not serving on.</summary>
    public string OverlayBaseUrl => $"http://127.0.0.1:{OverlayPort}";

    /// <summary>
    /// Set while the overlay host is not serving: the main window shows this reason beside a Retry button, so
    /// a failed start — or a host that died mid-session — is visible instead of only being a line in the log.
    /// </summary>
    [ObservableProperty]
    private string? overlayStatus;

    [ObservableProperty]
    private bool overlayIsOffline;

    /// <summary>True once the match has a winner — the only time its record can be saved.</summary>
    [ObservableProperty]
    private bool matchWon;

    /// <summary>True while the history already holds this match, so the button cannot file it twice.</summary>
    [ObservableProperty]
    private bool matchSaved;

    /// <summary>What the save button enables on: a finished match that is not in the history yet.</summary>
    public bool CanSaveMatch => MatchWon && !MatchSaved;

    public bool IsEightBall => SelectedGameType == GameType.EightBall;

    public bool IsSplitRaceTo => SelectedRaceToMode == RaceToMode.Split;

    /// <summary>
    /// The one constructor, wired by <see cref="MainWindow"/>: it passes its own window and dispatcher, and tests
    /// pass a roster store rooted in a throwaway folder and no dispatcher, which means "apply state changes
    /// inline". Nothing here reads <see cref="Application.Current"/>, so a view model can be built without a
    /// running app — which is what makes the state the screen shows for a fresh launch or a resumed match
    /// assertable.
    /// <para>
    /// Internal on purpose: a caller that left the dispatcher out would get a view model writing WPF-bound state
    /// from whatever thread raised the event, the Kestrel control API's threads included.
    /// </para>
    /// </summary>
    internal GameViewModel(
        GameManager gameManager,
        Action? persist,
        IOverlaySession? overlaySession,
        PlayerRosterStore rosterStore,
        Dispatcher? dispatcher,
        Window? owner,
        MatchHistoryStore? historyStore = null)
    {
        _gameManager = gameManager;
        _persist = persist;
        _overlaySession = overlaySession;
        _rosterStore = rosterStore;
        _historyStore = historyStore ?? new MatchHistoryStore();
        _dispatcher = dispatcher;
        _owner = owner;
        _gameManager.GameStateChanged += OnGameStateChanged;

        // The host can stop serving after a successful start too, and the session probes for that, so the
        // banner is not left showing a stale "serving" state for the rest of the match.
        if (_overlaySession != null)
            _overlaySession.Stopped += OnOverlayStopped;

        // Start from whatever GameManager already holds: App restores the saved operator profile into
        // it before this window exists, so the screen has to show those values rather than defaults.
        // The change handlers are suppressed via _restoring below — state already agrees, so pushing
        // each property back through SetColorTheme/SetScoreboardStyle would only re-raise it.
        _roster = _rosterStore.Load();

        // The saved port was handed to the session before this window existed; showing it here means the field and
        // the Help window name the address the overlay really answers on.
        OverlayPort = _overlaySession?.Port ?? OperatorProfile.DefaultOverlayPort;

        // Game type and Race-To mode are per-match choices rather than part of the saved profile, but a
        // resumed match carries its own, and this screen, the ball layout and the overlay preview all read
        // them back from state.
        var restored = _gameManager.GetStateSnapshot();

        _restoring = true;
        try
        {
            ApplyRestoredLook(restored);
        }
        finally
        {
            _restoring = false;
        }

        BuildBalls();

        // Only a resumed match carries choices worth honouring. A state at setup holds the enum default —
        // 8-ball — which is not something anyone picked, so this screen's own default stays in charge of the
        // game type and Race-To mode exactly as it did before matches were resumed. Set after the balls exist:
        // both change handlers refresh the ball layout, which reads them.
        var resumed = restored.IsGameActive || restored.Winner != null;
        if (resumed)
        {
            SelectedGameType = restored.GameType;
            SelectedRaceToMode = restored.RaceToMode;
        }

        SetupPlayers(restored);
        RefreshSavedPlayers();
        ApplyMatchPreview();

        // A resumed match arrives with scores, groups, pocketed balls and a shooter already set, and nothing
        // raises an event until the operator touches something — so the live view is filled here from the same
        // snapshot the event handlers use. A state at setup has nothing beyond the preview pushed above.
        if (resumed)
            ApplyGameState(restored);
        else
            RefreshBallLayout();

        RefreshOverlayStatus();
    }

    private void ApplyRestoredLook(GameState restored)
    {
        HomeBackgroundColor = restored.ColorTheme.HomeBackground;
        AwayBackgroundColor = restored.ColorTheme.AwayBackground;
        HomeAccentColor = restored.ColorTheme.HomeAccent;
        AwayAccentColor = restored.ColorTheme.AwayAccent;
        HomeTextColor = restored.ColorTheme.HomeText;
        AwayTextColor = restored.ColorTheme.AwayText;

        CornerRoundness = restored.ScoreboardStyle.CornerRoundness;
        OverallScale = restored.ScoreboardStyle.OverallScale;
        GlossyFinish = restored.ScoreboardStyle.GlossyFinish;
        SelectedEndCapStyle = restored.ScoreboardStyle.EndCapStyle;
        SelectedShooterIndicatorStyle = restored.ScoreboardStyle.ShooterIndicatorStyle;

        ScoreBarVisible = restored.Visibility.ScoreBarVisible;
        BallTrackerVisible = restored.Visibility.BallTrackerVisible;
        WinnerBannerVisible = restored.Visibility.WinnerBannerVisible;

        // A resumed match carries the lengths it was started with, so the fields show what the clock will really
        // use rather than the defaults a fresh state would have offered.
        ShotClockSeconds = restored.ShotClockSeconds;
        ShotClockExtensionSeconds = restored.ShotClockExtensionSeconds;
    }

    partial void OnSelectedGameTypeChanged(GameType oldValue, GameType newValue)
    {
        OnPropertyChanged(nameof(IsEightBall));
        RefreshBallLayout();
        ApplyMatchPreview();
    }

    partial void OnSelectedRaceToModeChanged(RaceToMode oldValue, RaceToMode newValue)
    {
        OnPropertyChanged(nameof(IsSplitRaceTo));

        if (newValue == RaceToMode.Single && HomePlayer != null && AwayPlayer != null)
        {
            AwayPlayer.RaceToTarget = HomePlayer.RaceToTarget;
        }

        ApplyMatchPreview();
    }

    private void ApplyMatchPreview() => _gameManager.SetMatchPreview(SelectedGameType, SelectedRaceToMode);

    partial void OnShotClockSecondsChanged(int oldValue, int newValue) => ApplyShotClockLengths();

    partial void OnShotClockExtensionSecondsChanged(int oldValue, int newValue) => ApplyShotClockLengths();

    private void ApplyShotClockLengths()
    {
        if (_restoring)
            return;

        // Both together: either field's change is the same two-number setup write, and sending the other one
        // along costs nothing while a missing one would leave the clock on a length the screen does not show.
        _gameManager.SetShotClockLengths(ShotClockSeconds, ShotClockExtensionSeconds);

        // GameManager clamps rather than refuses, so a typo would otherwise leave the field claiming one length
        // while the clock counted down another. Showing back what it accepted — on leaving the field, which is
        // when these bind — makes that visible. Writing a property from its own change handler re-enters once with
        // an in-range value, which then agrees with state and stops.
        var accepted = _gameManager.GetStateSnapshot();

        if (accepted.ShotClockSeconds != ShotClockSeconds)
            ShotClockSeconds = accepted.ShotClockSeconds;

        if (accepted.ShotClockExtensionSeconds != ShotClockExtensionSeconds)
            ShotClockExtensionSeconds = accepted.ShotClockExtensionSeconds;
    }

    partial void OnHomeBackgroundColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    partial void OnAwayBackgroundColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    partial void OnHomeAccentColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    partial void OnAwayAccentColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    partial void OnHomeTextColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    partial void OnAwayTextColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    private void ApplyColorTheme()
    {
        if (_restoring)
            return;

        _gameManager.SetColorTheme(new ColorTheme
        {
            HomeBackground = HomeBackgroundColor,
            AwayBackground = AwayBackgroundColor,
            HomeAccent = HomeAccentColor,
            AwayAccent = AwayAccentColor,
            HomeText = HomeTextColor,
            AwayText = AwayTextColor
        });

        _persist?.Invoke();
    }

    [RelayCommand]
    private void PickColor(string? colorKey)
    {
        if (colorKey == null)
            return;

        var current = colorKey switch
        {
            "HomeBackground" => HomeBackgroundColor,
            "AwayBackground" => AwayBackgroundColor,
            "HomeAccent" => HomeAccentColor,
            "AwayAccent" => AwayAccentColor,
            "HomeText" => HomeTextColor,
            "AwayText" => AwayTextColor,
            _ => (string?)null
        };
        if (current == null)
            return;

        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(current);
            dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        }
        catch (Exception)
        {
            // Operator is mid-edit of the hex value; open the dialog with its own default instead.
        }

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        switch (colorKey)
        {
            case "HomeBackground": HomeBackgroundColor = hex; break;
            case "AwayBackground": AwayBackgroundColor = hex; break;
            case "HomeAccent": HomeAccentColor = hex; break;
            case "AwayAccent": AwayAccentColor = hex; break;
            case "HomeText": HomeTextColor = hex; break;
            case "AwayText": AwayTextColor = hex; break;
        }
    }

    partial void OnCornerRoundnessChanged(int oldValue, int newValue) => ApplyScoreboardStyle();

    partial void OnOverallScaleChanged(int oldValue, int newValue) => ApplyScoreboardStyle();

    partial void OnGlossyFinishChanged(bool oldValue, bool newValue) => ApplyScoreboardStyle();

    partial void OnSelectedEndCapStyleChanged(EndCapStyle oldValue, EndCapStyle newValue) => ApplyScoreboardStyle();

    partial void OnSelectedShooterIndicatorStyleChanged(ShooterIndicatorStyle oldValue, ShooterIndicatorStyle newValue) => ApplyScoreboardStyle();

    private void ApplyScoreboardStyle()
    {
        if (_restoring)
            return;

        _gameManager.SetScoreboardStyle(new ScoreboardStyle
        {
            CornerRoundness = CornerRoundness,
            OverallScale = OverallScale,
            GlossyFinish = GlossyFinish,
            EndCapStyle = SelectedEndCapStyle,
            ShooterIndicatorStyle = SelectedShooterIndicatorStyle
        });

        _persist?.Invoke();
    }

    /// <summary>
    /// Tries the overlay host again after a failed start — usually another process held 51234 and has since
    /// gone away. Success clears the banner; failure updates it with the current reason.
    /// </summary>
    [RelayCommand]
    private void RetryOverlay()
    {
        _overlaySession?.Start(_gameManager);
        RefreshOverlayStatus();
    }

    /// <summary>
    /// The host stopped on its own. This runs on whichever thread stopped it (a Kestrel failure), and the
    /// banner is WPF-bound state, so it hops to the UI thread the way game-state events do.
    /// </summary>
    private void OnOverlayStopped(object? sender, EventArgs e)
    {
        var dispatcher = _dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            try
            {
                if (!dispatcher.HasShutdownStarted)
                    dispatcher.BeginInvoke(new Action(RefreshOverlayStatus));
            }
            catch (InvalidOperationException)
            {
                // Shutdown began between the check and the post; the window is going away anyway.
            }

            return;
        }

        RefreshOverlayStatus();
    }

    private void RefreshOverlayStatus()
    {
        if (_overlaySession is { } session && !session.IsRunning)
        {
            OverlayIsOffline = true;
            OverlayStatus = session.FailureReason;
            return;
        }

        OverlayIsOffline = false;
        OverlayStatus = null;
    }

    /// <summary>
    /// The operator changed the port: rebind there and say what happened. A port already in use is not an error to
    /// hide — the host starts, fails, and the banner names the new port and its new holder, with Retry beside it.
    /// </summary>
    partial void OnOverlayPortChanged(int oldValue, int newValue)
    {
        // OverlayBaseUrl is computed from this, so it has to announce its own change — ahead of the return
        // below, because a view model with no session (the shape the tests build) has an address that moves too.
        OnPropertyChanged(nameof(OverlayBaseUrl));

        if (_overlaySession is not { } session)
            return;

        if (!OperatorProfile.IsUsablePort(newValue))
        {
            // The field is the only editor, and a port outside the range cannot be bound without elevation or at
            // all: put back what the session is really using rather than leave the window advertising it.
            OverlayPort = session.Port;
            return;
        }

        if (newValue == session.Port)
            return;

        session.SetPort(newValue);
        session.Start(_gameManager);
        RefreshOverlayStatus();
        _persist?.Invoke();
    }

    [RelayCommand]
    private void StartMatch()
    {
        // Starting a match is when a name becomes a player who played, so both sides join the roster
        // here (blank sides are ignored) and the pickers come back with them at the top.
        RememberPlayers();
        _gameManager.InitializeGame(SelectedGameType, SelectedRaceToMode);
    }

    [RelayCommand]
    private void AddHomePoint() => _gameManager.AddPoint(isPlayer1: true);

    [RelayCommand]
    private void AddAwayPoint() => _gameManager.AddPoint(isPlayer1: false);

    [RelayCommand]
    private void SubtractHomePoint() => _gameManager.SubtractPoint(isPlayer1: true);

    [RelayCommand]
    private void SubtractAwayPoint() => _gameManager.SubtractPoint(isPlayer1: false);

    [RelayCommand]
    private void UndoLastPoint() => _gameManager.UndoPoint();

    [RelayCommand]
    private void ToggleCurrentShooter() => _gameManager.ToggleCurrentShooter();

    [RelayCommand]
    private void AssignHomeSolids() => _gameManager.AssignBallGroup(isHome: true, BallGroup.Solids);

    [RelayCommand]
    private void AssignHomeStripes() => _gameManager.AssignBallGroup(isHome: true, BallGroup.Stripes);

    [RelayCommand]
    private void AssignAwaySolids() => _gameManager.AssignBallGroup(isHome: false, BallGroup.Solids);

    [RelayCommand]
    private void AssignAwayStripes() => _gameManager.AssignBallGroup(isHome: false, BallGroup.Stripes);

    [RelayCommand]
    private void PickHomeIcon() => PickIcon(HomePlayer);

    [RelayCommand]
    private void PickAwayIcon() => PickIcon(AwayPlayer);

    [RelayCommand]
    private void ClearHomeIcon()
    {
        if (HomePlayer != null)
            HomePlayer.EndCapIconDataUri = null;
    }

    [RelayCommand]
    private void ClearAwayIcon()
    {
        if (AwayPlayer != null)
            AwayPlayer.EndCapIconDataUri = null;
    }

    [RelayCommand]
    private void PickHomeFlag() => PickFlag(HomePlayer);

    [RelayCommand]
    private void PickAwayFlag() => PickFlag(AwayPlayer);

    private void PickFlag(PlayerViewModel? player)
    {
        if (player == null)
            return;

        var picker = new FlagPickerWindow { Owner = _owner };
        if (picker.ShowDialog() != true || picker.SelectedEntry == null)
            return;

        var resourceStream = Application.GetResourceStream(picker.SelectedEntry.PackUri)
            ?? throw new InvalidOperationException($"Bundled flag resource not found: {picker.SelectedEntry.PackUri}");

        using var stream = resourceStream.Stream;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        player.EndCapIconDataUri = $"data:image/svg+xml;base64,{Convert.ToBase64String(memory.ToArray())}";
    }

    private static void PickIcon(PlayerViewModel? player)
    {
        if (player == null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Choose End-Cap Icon (Team Logo / Flag)",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp"
        };
        if (dialog.ShowDialog() != true)
            return;

        var bytes = File.ReadAllBytes(dialog.FileName);
        if (bytes.Length > 1_500_000)
        {
            MessageBox.Show("Please choose an image smaller than 1.5 MB.", "Icon too large",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mime = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
        player.EndCapIconDataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    [RelayCommand]
    private void TogglePocketed(BallItemViewModel? ball)
    {
        if (ball == null)
            return;

        if (ball.IsPocketed)
            _gameManager.UnpocketBall(ball.Number);
        else
            _gameManager.PocketBall(ball.Number);
    }

    [RelayCommand]
    private void ToggleScoreBarVisible()
    {
        _gameManager.SetScoreBarVisible(!ScoreBarVisible);
        _persist?.Invoke();
    }

    [RelayCommand]
    private void ToggleBallTrackerVisible()
    {
        _gameManager.SetBallTrackerVisible(!BallTrackerVisible);
        _persist?.Invoke();
    }

    [RelayCommand]
    private void ToggleWinnerBannerVisible()
    {
        _gameManager.SetWinnerBannerVisible(!WinnerBannerVisible);
        _persist?.Invoke();
    }

    [RelayCommand]
    private void NewRack() => _gameManager.ResetBalls();

    // Nothing to re-push afterwards: ResetGame keeps the player fields, the styling and the chosen
    // game type and Race-To mode, and raises a snapshot synchronously on this thread, so
    // ApplyGameState has already cleared the score, groups, winner and pocketed balls and the
    // overlay's preview already matches this screen.
    [RelayCommand]
    private void ResetMatch() => _gameManager.ResetGame();

    [RelayCommand]
    private void ShowHelp() => new HelpWindow(OverlayBaseUrl) { Owner = _owner }.ShowDialog();

    /// <summary>
    /// Starts or restarts the shot clock the overlay counts down. The operator presses it as each player settles
    /// over the ball — only they can tell when a shot has begun, which is why this is a key rather than something
    /// the score changes trigger on their own.
    /// </summary>
    [RelayCommand]
    private void StartShotClock() => _gameManager.StartShotClock();

    /// <summary>
    /// Calls the current shooter's extension on their behalf: the clock already running gains the extension time the
    /// setup screen chose. One per player per rack, and only while a clock is running — both rules are Core's, so
    /// this only has to reach it.
    /// </summary>
    [RelayCommand]
    private void ExtendShotClock() => _gameManager.ExtendShotClock();

    /// <summary>
    /// Files the finished match, by explicit request: the operator decides what belongs in the history, so a win
    /// on its own does not write one. Pressing it again for the same match files nothing — the archive refuses an
    /// id it already holds — which is also what covers a resumed match that was saved before the relaunch.
    /// </summary>
    [RelayCommand]
    private void SaveMatch()
    {
        var state = _gameManager.GetStateSnapshot();
        if (state.Winner is null)
            return;

        if (_historyStore.TryArchive(state, DateTimeOffset.Now) is null &&
            !_historyStore.Load().Contains(state.MatchId))
        {
            // Not archived and not present: the write failed and the store has logged why. The button stays
            // enabled, which is the honest state — the operator can press it again.
            return;
        }

        _historyCheckedMatchId = state.MatchId;
        MatchSaved = true;
    }

    partial void OnMatchWonChanged(bool value) => OnPropertyChanged(nameof(CanSaveMatch));

    partial void OnMatchSavedChanged(bool value) => OnPropertyChanged(nameof(CanSaveMatch));

    // The window loads from the store itself, so it can refresh after a match is deleted without the view model
    // holding a copy that has gone stale.
    [RelayCommand]
    private void ShowHistory()
    {
        new HistoryWindow(_historyStore) { Owner = _owner }.ShowDialog();

        // A record can be deleted from that window — this match's included — so the live view takes the answer
        // again rather than leaving "Saved to history" up for a save that no longer exists.
        RefreshSavedMatch(_gameManager.GetStateSnapshot(), reread: true);
    }

    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        // State changes can be raised from a Kestrel thread — the HTTP control API the Stream Deck
        // drives mutates the same GameManager the UI is bound to. Everything below touches
        // WPF-bound state and, through MainWindow's PropertyChanged handler, dependency objects,
        // which may only be written on the UI thread; without this hop those writes throw
        // "The calling thread cannot access this object" straight back out of the HTTP request as
        // a 500. The event payload is a snapshot, so it stays valid while the dispatch is queued.
        var dispatcher = _dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => ApplyGameState(e.GameState)));
            return;
        }

        ApplyGameState(e.GameState);
    }

    /// <summary>
    /// What the save control says about the match in state: whether there is a winner to file, and whether the
    /// history already holds it.
    /// <para>
    /// Reading the file costs one look per match — at the moment it is won — because the rest of the time the answer
    /// cannot have changed. <paramref name="reread"/> is for the one case where it can: the History window can delete
    /// this match's record while its banner is up behind the dialog, so the answer is taken again once it closes.
    /// </para>
    /// </summary>
    internal void RefreshSavedMatch(GameState state, bool reread = false)
    {
        MatchWon = state.Winner is not null;

        if (!MatchWon)
        {
            // No winner, nothing to save, and the next one is a different match.
            _historyCheckedMatchId = null;
            MatchSaved = false;
            return;
        }

        if (!reread && string.Equals(_historyCheckedMatchId, state.MatchId, StringComparison.Ordinal))
            return;

        // A resumed match is the ordinary case this exists for: the record was written by a previous run, so
        // nothing in this one remembers saving it. A deletion behind the dialog is the other.
        _historyCheckedMatchId = state.MatchId;
        MatchSaved = _historyStore.Load().Contains(state.MatchId);
    }

    private void ApplyGameState(GameState state)
    {
        // Derived rather than only set by local button clicks — a remote Stream Deck call to
        // /api/control/match/reset mutates GameManager directly, so this is what lets the
        // Controller's own screen flip back to Match Setup in that case too. A match with a winner
        // still counts as "initialized" so the winner line stays on the live view until the operator
        // resets or starts a new rack — a win no longer deactivates the match.
        GameInitialized = state.IsGameActive || state.Winner != null;

        HomeScore = state.Player1Score;
        AwayScore = state.Player2Score;
        HomeIsCurrentShooter = ReferenceEquals(state.CurrentShooter, state.Player1);
        HomeBallGroup = state.Player1.BallGroup;
        AwayBallGroup = state.Player2.BallGroup;
        // An unnamed winning player would otherwise render " wins!", so fall back to their side —
        // the same fallback the overlay's name pills and winner banner use. GameManager trims names
        // on write, so empty is the only blank case that can arrive here.
        WinnerAnnouncement = state.Winner == null
            ? null
            : $"{(string.IsNullOrEmpty(state.Winner.Name) ? (ReferenceEquals(state.Winner, state.Player1) ? "Home" : "Away") : state.Winner.Name)} wins!";

        RefreshSavedMatch(state);

        ScoreBarVisible = state.Visibility.ScoreBarVisible;
        BallTrackerVisible = state.Visibility.BallTrackerVisible;
        WinnerBannerVisible = state.Visibility.WinnerBannerVisible;

        foreach (var (number, ball) in _allBalls)
            ball.IsPocketed = state.PocketedBalls.Contains(number);

        RefreshBallLayout();
    }

    private void SetupPlayers(GameState restored)
    {
        HomePlayer = new PlayerViewModel(_gameManager, isHome: true, restored.Player1, _persist, _rosterStore.LoadIcon);
        AwayPlayer = new PlayerViewModel(_gameManager, isHome: false, restored.Player2, _persist, _rosterStore.LoadIcon);

        // Only for the Race-To mirror: the fields themselves already reach state (and the overlay)
        // through SetPlayerDetails, so pushing the match preview on every keystroke would just
        // re-send an unchanged game type and Race-To mode.
        HomePlayer.PropertyChanged += OnHomePlayerPropertyChanged;
    }

    /// <summary>
    /// Saves both sides into the roster (players with a name only), writing their end-cap artwork
    /// alongside, then refreshes the pickers. Called when a match starts and when the window closes.
    /// </summary>
    public void RememberPlayers()
    {
        foreach (var player in new[] { HomePlayer, AwayPlayer })
        {
            if (player == null)
                continue;

            var entry = _roster.Remember(
                player.PlayerName,
                player.TeamName,
                player.RaceToTarget,
                Guid.NewGuid().ToString("n"));

            if (entry == null)
                continue;

            _rosterStore.SetIcon(entry.Id, player.EndCapIconDataUri);
        }

        _rosterStore.TrySave(_roster);
        RefreshSavedPlayers();
    }

    /// <summary>
    /// Removes one saved player, so a mis-typed name can be cleared out instead of lingering in every
    /// side's picker. The roster rewrites its file without that entry, and the next save drops the
    /// player's artwork with it.
    /// </summary>
    [RelayCommand]
    private void ForgetSavedPlayer(SavedPlayerOption? option)
    {
        if (option == null || !_roster.Forget(option.Id))
            return;

        // A failed write leaves the file as it was: the entry is gone for this session only, which
        // beats a crash from clicking Delete while something holds roster.json open.
        _rosterStore.TrySave(_roster);
        RefreshSavedPlayers();
    }

    private void RefreshSavedPlayers()
    {
        // Selections are cleared first: the list items are replaced wholesale, and a stale selection
        // would otherwise fill the fields again the moment the operator's new objects arrive.
        if (HomePlayer != null)
            HomePlayer.SavedPlayer = null;
        if (AwayPlayer != null)
            AwayPlayer.SavedPlayer = null;

        SavedPlayers.Clear();
        foreach (var entry in _roster.Players)
            SavedPlayers.Add(new SavedPlayerOption(entry.Id, entry.Name, entry.TeamName, entry.RaceToTarget));
    }

    private void OnHomePlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.RaceToTarget)
            && SelectedRaceToMode == RaceToMode.Single
            && HomePlayer != null && AwayPlayer != null)
        {
            AwayPlayer.RaceToTarget = HomePlayer.RaceToTarget;
        }
    }

    private void BuildBalls()
    {
        for (var number = 1; number <= 15; number++)
        {
            var colorKey = number > 8 ? number - 8 : number;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BaseBallColorHex[colorKey]));
            brush.Freeze();
            _allBalls[number] = new BallItemViewModel(number, brush);
        }
    }

    private void RefreshBallLayout()
    {
        if (SelectedGameType == GameType.EightBall)
        {
            EightBallItems = new[] { _allBalls[8] };
            HomeGroupBalls = BallsForGroup(HomeBallGroup);
            AwayGroupBalls = BallsForGroup(AwayBallGroup);
            NumericRowBalls = Array.Empty<BallItemViewModel>();
        }
        else
        {
            EightBallItems = Array.Empty<BallItemViewModel>();
            HomeGroupBalls = Array.Empty<BallItemViewModel>();
            AwayGroupBalls = Array.Empty<BallItemViewModel>();

            var highBall = SelectedGameType == GameType.TenBall ? 10 : 9;
            NumericRowBalls = Enumerable.Range(1, highBall).Select(n => _allBalls[n]).ToList();
        }
    }

    private IReadOnlyList<BallItemViewModel> BallsForGroup(BallGroup group) => group switch
    {
        BallGroup.Solids => Enumerable.Range(1, 7).Select(n => _allBalls[n]).ToList(),
        BallGroup.Stripes => Enumerable.Range(9, 7).Select(n => _allBalls[n]).ToList(),
        _ => Array.Empty<BallItemViewModel>()
    };
}
