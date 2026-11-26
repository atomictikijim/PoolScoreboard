using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
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
    private readonly PlayerRosterStore _rosterStore = new();
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

    public bool IsEightBall => SelectedGameType == GameType.EightBall;

    public bool IsSplitRaceTo => SelectedRaceToMode == RaceToMode.Split;

    public GameViewModel(GameManager gameManager, Action? persist = null)
    {
        _gameManager = gameManager;
        _persist = persist;
        _gameManager.GameStateChanged += OnGameStateChanged;

        // Start from whatever GameManager already holds: App restores the saved operator profile into
        // it before this window exists, so the screen has to show those values rather than defaults.
        // The change handlers are suppressed via _restoring below — state already agrees, so pushing
        // each property back through SetColorTheme/SetScoreboardStyle would only re-raise it.
        _roster = _rosterStore.Load();

        // Game type and Race-To mode are per-match choices, not part of the saved profile, so this
        // screen's own defaults stay in charge of them (ApplyMatchPreview below pushes them).
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
        SetupPlayers(restored);
        RefreshSavedPlayers();
        RefreshBallLayout();
        ApplyMatchPreview();
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

    private static void PickFlag(PlayerViewModel? player)
    {
        if (player == null)
            return;

        var picker = new FlagPickerWindow { Owner = Application.Current.MainWindow };
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
    private void ShowHelp() => new HelpWindow { Owner = Application.Current.MainWindow }.ShowDialog();

    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        // State changes can be raised from a Kestrel thread — the HTTP control API the Stream Deck
        // drives mutates the same GameManager the UI is bound to. Everything below touches
        // WPF-bound state and, through MainWindow's PropertyChanged handler, dependency objects,
        // which may only be written on the UI thread; without this hop those writes throw
        // "The calling thread cannot access this object" straight back out of the HTTP request as
        // a 500. The event payload is a snapshot, so it stays valid while the dispatch is queued.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => ApplyGameState(e.GameState)));
            return;
        }

        ApplyGameState(e.GameState);
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

        _rosterStore.Save(_roster);
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
