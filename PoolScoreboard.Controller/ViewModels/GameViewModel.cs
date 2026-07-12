using CommunityToolkit.Mvvm.ComponentModel;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Controller.ViewModels;

public partial class GameViewModel : ObservableObject
{
    private GameManager? _gameManager;

    [ObservableProperty]
    private League selectedLeague = League.APA;

    [ObservableProperty]
    private GameType selectedGameType = GameType.NineBall;

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

    public GameViewModel()
    {
        SelectedLeague = League.APA;
        SelectedGameType = GameType.NineBall;
    }

    public void InitializeGame()
    {
        if (HomePlayer == null || AwayPlayer == null)
            return;

        _gameManager = new GameManager();

        var homePlayerModel = new Player
        {
            Name = HomePlayer.PlayerName,
            TeamName = HomePlayer.TeamName,
            SkillLevel = HomePlayer.SkillLevel,
            League = SelectedLeague
        };

        var awayPlayerModel = new Player
        {
            Name = AwayPlayer.PlayerName,
            TeamName = AwayPlayer.TeamName,
            SkillLevel = AwayPlayer.SkillLevel,
            League = SelectedLeague
        };

        _gameManager.InitializeGame(homePlayerModel, awayPlayerModel, SelectedGameType);
        _gameManager.GameStateChanged += OnGameStateChanged;

        GameInitialized = true;
    }

    public void StartNewGame()
    {
        if (HomePlayer != null && AwayPlayer != null)
        {
            HomePlayer.GameScore = 0;
            AwayPlayer.GameScore = 0;
            HomePlayer.IsAtTable = true;
            AwayPlayer.IsAtTable = false;
            InitializeGame();
        }
    }

    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        // Update UI based on game state changes
        // This will be extended as we add more game tracking
    }

    public void SetupPlayers(League league, GameType gameType)
    {
        SelectedLeague = league;
        SelectedGameType = gameType;

        HomePlayer = new PlayerViewModel(league, gameType);
        AwayPlayer = new PlayerViewModel(league, gameType);
    }
}
