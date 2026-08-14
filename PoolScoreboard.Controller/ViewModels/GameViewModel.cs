using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Controller.ViewModels;

public partial class GameViewModel : ObservableObject
{
    private GameManager? _gameManager;

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
    private bool gameInitialized = false;

    [ObservableProperty]
    private string accentColor = "#00d4ff";

    [ObservableProperty]
    private bool homeIsCurrentShooter = true;

    public bool IsEightBall => SelectedGameType == GameType.EightBall;

    public bool IsSplitRaceTo => SelectedRaceToMode == RaceToMode.Split;

    public GameViewModel()
    {
        SetupPlayers();
    }

    partial void OnSelectedGameTypeChanged(GameType oldValue, GameType newValue)
    {
        OnPropertyChanged(nameof(IsEightBall));

        if (newValue != GameType.EightBall && HomePlayer != null && AwayPlayer != null)
        {
            HomePlayer.BallGroup = BallGroup.Unassigned;
            AwayPlayer.BallGroup = BallGroup.Unassigned;
        }
    }

    partial void OnSelectedRaceToModeChanged(RaceToMode oldValue, RaceToMode newValue)
    {
        OnPropertyChanged(nameof(IsSplitRaceTo));

        if (newValue == RaceToMode.Single && HomePlayer != null && AwayPlayer != null)
        {
            AwayPlayer.RaceToTarget = HomePlayer.RaceToTarget;
        }
    }

    public void InitializeGame()
    {
        if (HomePlayer == null || AwayPlayer == null)
            return;

        _gameManager = new GameManager();
        _gameManager.InitializeGame(HomePlayer.GetPlayer(), AwayPlayer.GetPlayer(), SelectedGameType, SelectedRaceToMode);
        _gameManager.GameStateChanged += OnGameStateChanged;

        GameInitialized = true;
    }

    public void StartNewGame()
    {
        if (HomePlayer != null && AwayPlayer != null)
        {
            HomePlayer.Score = 0;
            AwayPlayer.Score = 0;
            HomeIsCurrentShooter = true;
            InitializeGame();
        }
    }

    [RelayCommand]
    public void ToggleCurrentShooter()
    {
        HomeIsCurrentShooter = !HomeIsCurrentShooter;
    }

    [RelayCommand]
    public void AssignHomeSolids() => AssignGroup(HomePlayer, AwayPlayer, BallGroup.Solids);

    [RelayCommand]
    public void AssignHomeStripes() => AssignGroup(HomePlayer, AwayPlayer, BallGroup.Stripes);

    [RelayCommand]
    public void AssignAwaySolids() => AssignGroup(AwayPlayer, HomePlayer, BallGroup.Solids);

    [RelayCommand]
    public void AssignAwayStripes() => AssignGroup(AwayPlayer, HomePlayer, BallGroup.Stripes);

    private static void AssignGroup(PlayerViewModel? player, PlayerViewModel? other, BallGroup group)
    {
        if (player == null || other == null)
            return;

        player.BallGroup = group;
        other.BallGroup = group switch
        {
            BallGroup.Solids => BallGroup.Stripes,
            BallGroup.Stripes => BallGroup.Solids,
            _ => BallGroup.Unassigned
        };
    }

    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        // Update UI based on game state changes
        // This will be extended as we add more game tracking
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
}
