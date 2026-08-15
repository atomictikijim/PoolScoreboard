using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

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
    private readonly Dictionary<int, BallItemViewModel> _allBalls = new();

    private Player? _homePlayerModel;
    private Player? _awayPlayerModel;

    // --- Match setup (editable before the match starts) ---

    [ObservableProperty]
    private GameType selectedGameType = GameType.NineBall;

    [ObservableProperty]
    private RaceToMode selectedRaceToMode = RaceToMode.Single;

    [ObservableProperty]
    private string? matchTitle;

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
    private string textColor = "#f0f0f0";

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
    private bool homeIsCurrentShooter = true;

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

    public GameViewModel(GameManager gameManager)
    {
        _gameManager = gameManager;
        _gameManager.GameStateChanged += OnGameStateChanged;

        BuildBalls();
        SetupPlayers();
        RefreshBallLayout();
        ApplyMatchPreview();
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

    private void ApplyMatchPreview()
    {
        if (HomePlayer == null || AwayPlayer == null)
            return;

        _gameManager.SetMatchPreview(HomePlayer.GetPlayer(), AwayPlayer.GetPlayer(), SelectedGameType, SelectedRaceToMode);
    }

    partial void OnHomeBackgroundColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    partial void OnAwayBackgroundColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    partial void OnHomeAccentColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    partial void OnAwayAccentColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    partial void OnTextColorChanged(string? oldValue, string newValue) => ApplyColorTheme();

    private void ApplyColorTheme()
    {
        _gameManager.SetColorTheme(new ColorTheme
        {
            HomeBackground = HomeBackgroundColor,
            AwayBackground = AwayBackgroundColor,
            HomeAccent = HomeAccentColor,
            AwayAccent = AwayAccentColor,
            Text = TextColor
        });
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
            "Text" => TextColor,
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
            case "Text": TextColor = hex; break;
        }
    }

    partial void OnCornerRoundnessChanged(int oldValue, int newValue) => ApplyScoreboardStyle();

    partial void OnOverallScaleChanged(int oldValue, int newValue) => ApplyScoreboardStyle();

    partial void OnGlossyFinishChanged(bool oldValue, bool newValue) => ApplyScoreboardStyle();

    partial void OnSelectedEndCapStyleChanged(EndCapStyle oldValue, EndCapStyle newValue) => ApplyScoreboardStyle();

    partial void OnSelectedShooterIndicatorStyleChanged(ShooterIndicatorStyle oldValue, ShooterIndicatorStyle newValue) => ApplyScoreboardStyle();

    private void ApplyScoreboardStyle()
    {
        _gameManager.SetScoreboardStyle(new ScoreboardStyle
        {
            CornerRoundness = CornerRoundness,
            OverallScale = OverallScale,
            GlossyFinish = GlossyFinish,
            EndCapStyle = SelectedEndCapStyle,
            ShooterIndicatorStyle = SelectedShooterIndicatorStyle
        });
    }

    [RelayCommand]
    private void StartMatch()
    {
        if (HomePlayer == null || AwayPlayer == null)
            return;

        _homePlayerModel = HomePlayer.GetPlayer();
        _awayPlayerModel = AwayPlayer.GetPlayer();

        _gameManager.InitializeGame(_homePlayerModel, _awayPlayerModel, SelectedGameType, SelectedRaceToMode);
        ApplyColorTheme();
        ApplyScoreboardStyle();
    }

    [RelayCommand]
    private void AddHomePoint() => _gameManager.AddPoint(isPlayer1: true);

    [RelayCommand]
    private void AddAwayPoint() => _gameManager.AddPoint(isPlayer1: false);

    [RelayCommand]
    private void UndoLastPoint() => _gameManager.UndoPoint();

    [RelayCommand]
    private void ToggleCurrentShooter()
    {
        if (_homePlayerModel == null || _awayPlayerModel == null)
            return;

        _gameManager.SetCurrentShooter(HomeIsCurrentShooter ? _awayPlayerModel : _homePlayerModel);
    }

    [RelayCommand]
    private void AssignHomeSolids() => AssignGroup(_homePlayerModel, BallGroup.Solids);

    [RelayCommand]
    private void AssignHomeStripes() => AssignGroup(_homePlayerModel, BallGroup.Stripes);

    [RelayCommand]
    private void AssignAwaySolids() => AssignGroup(_awayPlayerModel, BallGroup.Solids);

    [RelayCommand]
    private void AssignAwayStripes() => AssignGroup(_awayPlayerModel, BallGroup.Stripes);

    private void AssignGroup(Player? player, BallGroup group)
    {
        if (player == null)
            return;

        _gameManager.AssignBallGroup(player, group);
    }

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
    private void ToggleScoreBarVisible() => _gameManager.SetScoreBarVisible(!ScoreBarVisible);

    [RelayCommand]
    private void ToggleBallTrackerVisible() => _gameManager.SetBallTrackerVisible(!BallTrackerVisible);

    [RelayCommand]
    private void ToggleWinnerBannerVisible() => _gameManager.SetWinnerBannerVisible(!WinnerBannerVisible);

    [RelayCommand]
    private void NewRack() => _gameManager.ResetBalls();

    [RelayCommand]
    private void ResetMatch()
    {
        _gameManager.ResetGame();
        ApplyColorTheme();
        ApplyScoreboardStyle();
        ApplyMatchPreview();

        _homePlayerModel = null;
        _awayPlayerModel = null;
        HomeScore = 0;
        AwayScore = 0;
        HomeIsCurrentShooter = true;
        HomeBallGroup = BallGroup.Unassigned;
        AwayBallGroup = BallGroup.Unassigned;
        WinnerAnnouncement = null;

        foreach (var ball in _allBalls.Values)
            ball.IsPocketed = false;

        RefreshBallLayout();
    }

    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        var state = e.GameState;

        // Derived rather than only set by local button clicks — a remote Stream Deck call to
        // /api/control/match/reset mutates GameManager directly, so this is what lets the
        // Controller's own screen flip back to Match Setup in that case too. A finished match
        // (Winner set, IsGameActive false) still counts as "initialized" so the winner banner
        // stays on the live view until the operator resets or starts a new rack.
        GameInitialized = state.IsGameActive || state.Winner != null;

        HomeScore = state.Player1Score;
        AwayScore = state.Player2Score;
        HomeIsCurrentShooter = ReferenceEquals(state.CurrentShooter, state.Player1);
        HomeBallGroup = state.Player1.BallGroup;
        AwayBallGroup = state.Player2.BallGroup;
        WinnerAnnouncement = state.Winner == null ? null : $"{state.Winner.Name} wins!";

        ScoreBarVisible = state.Visibility.ScoreBarVisible;
        BallTrackerVisible = state.Visibility.BallTrackerVisible;
        WinnerBannerVisible = state.Visibility.WinnerBannerVisible;

        foreach (var (number, ball) in _allBalls)
            ball.IsPocketed = state.PocketedBalls.Contains(number);

        RefreshBallLayout();
    }

    private void SetupPlayers()
    {
        HomePlayer = new PlayerViewModel();
        AwayPlayer = new PlayerViewModel();
        HomePlayer.PropertyChanged += OnHomePlayerPropertyChanged;
        AwayPlayer.PropertyChanged += OnAwayPlayerPropertyChanged;
    }

    private void OnHomePlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.RaceToTarget)
            && SelectedRaceToMode == RaceToMode.Single
            && HomePlayer != null && AwayPlayer != null)
        {
            AwayPlayer.RaceToTarget = HomePlayer.RaceToTarget;
        }

        ApplyMatchPreview();
    }

    private void OnAwayPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyMatchPreview();

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
