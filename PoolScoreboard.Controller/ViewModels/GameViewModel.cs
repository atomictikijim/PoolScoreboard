using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private string backgroundColor = "#1a2332";

    [ObservableProperty]
    private string accentColor = "#00d4ff";

    [ObservableProperty]
    private string textColor = "#f0f0f0";

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

    public bool IsEightBall => SelectedGameType == GameType.EightBall;

    public bool IsSplitRaceTo => SelectedRaceToMode == RaceToMode.Split;

    public GameViewModel(GameManager gameManager)
    {
        _gameManager = gameManager;
        _gameManager.GameStateChanged += OnGameStateChanged;

        BuildBalls();
        SetupPlayers();
        RefreshBallLayout();
    }

    partial void OnSelectedGameTypeChanged(GameType oldValue, GameType newValue)
    {
        OnPropertyChanged(nameof(IsEightBall));
        RefreshBallLayout();
    }

    partial void OnSelectedRaceToModeChanged(RaceToMode oldValue, RaceToMode newValue)
    {
        OnPropertyChanged(nameof(IsSplitRaceTo));

        if (newValue == RaceToMode.Single && HomePlayer != null && AwayPlayer != null)
        {
            AwayPlayer.RaceToTarget = HomePlayer.RaceToTarget;
        }
    }

    [RelayCommand]
    private void StartMatch()
    {
        if (HomePlayer == null || AwayPlayer == null)
            return;

        _homePlayerModel = HomePlayer.GetPlayer();
        _awayPlayerModel = AwayPlayer.GetPlayer();

        _gameManager.InitializeGame(_homePlayerModel, _awayPlayerModel, SelectedGameType, SelectedRaceToMode);
        _gameManager.SetColorTheme(new ColorTheme
        {
            Background = BackgroundColor,
            Accent = AccentColor,
            Text = TextColor
        });

        GameInitialized = true;
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
    private void NewRack() => _gameManager.ResetBalls();

    [RelayCommand]
    private void ResetMatch()
    {
        _gameManager.ResetGame();

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
        GameInitialized = false;
    }

    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        var state = e.GameState;

        HomeScore = state.Player1Score;
        AwayScore = state.Player2Score;
        HomeIsCurrentShooter = ReferenceEquals(state.CurrentShooter, state.Player1);
        HomeBallGroup = state.Player1.BallGroup;
        AwayBallGroup = state.Player2.BallGroup;
        WinnerAnnouncement = state.Winner == null ? null : $"{state.Winner.Name} wins!";

        foreach (var (number, ball) in _allBalls)
            ball.IsPocketed = state.PocketedBalls.Contains(number);

        RefreshBallLayout();
    }

    private void SetupPlayers()
    {
        HomePlayer = new PlayerViewModel();
        AwayPlayer = new PlayerViewModel();
        HomePlayer.PropertyChanged += OnHomePlayerPropertyChanged;
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
