using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Rules;

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

    [ObservableProperty]
    private bool homeAtTable = true;

    public GameViewModel()
    {
        SelectedLeague = League.APA;
        SelectedGameType = GameType.NineBall;
        SetupPlayers(SelectedLeague, SelectedGameType);
    }

    partial void OnSelectedLeagueChanged(League oldValue, League newValue)
    {
        // Update league without recreating players (preserves skill levels)
        if (HomePlayer != null && AwayPlayer != null)
        {
            HomePlayer.UpdateLeagueAndGameType(newValue, SelectedGameType);
            AwayPlayer.UpdateLeagueAndGameType(newValue, SelectedGameType);
        }
    }

    partial void OnSelectedGameTypeChanged(GameType oldValue, GameType newValue)
    {
        // Update game type without recreating players (preserves skill levels)
        if (HomePlayer != null && AwayPlayer != null)
        {
            HomePlayer.UpdateLeagueAndGameType(SelectedLeague, newValue);
            AwayPlayer.UpdateLeagueAndGameType(SelectedLeague, newValue);
        }
    }

    public void InitializeGame()
    {
        if (HomePlayer == null || AwayPlayer == null)
            return;

        _gameManager = new GameManager();

        var skillLevel = HomePlayer.UsesSkillLevel ? HomePlayer.SkillLevel : (HomePlayer.FargoRating / 100);
        var homePlayerModel = new Player
        {
            Name = HomePlayer.PlayerName,
            TeamName = HomePlayer.TeamName,
            SkillLevel = skillLevel,
            League = SelectedLeague
        };

        skillLevel = AwayPlayer.UsesSkillLevel ? AwayPlayer.SkillLevel : (AwayPlayer.FargoRating / 100);
        var awayPlayerModel = new Player
        {
            Name = AwayPlayer.PlayerName,
            TeamName = AwayPlayer.TeamName,
            SkillLevel = skillLevel,
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
            HomeAtTable = true;
            InitializeGame();
        }
    }

    [RelayCommand]
    public void ToggleAtTable()
    {
        HomeAtTable = !HomeAtTable;
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
