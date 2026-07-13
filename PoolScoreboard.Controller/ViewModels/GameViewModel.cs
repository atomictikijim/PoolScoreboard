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
        SetupPlayers(newValue, SelectedGameType);
    }

    partial void OnSelectedGameTypeChanged(GameType oldValue, GameType newValue)
    {
        SetupPlayers(SelectedLeague, newValue);
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

    public string HomePlayerRaceToDisplay
    {
        get
        {
            if (HomePlayer == null || AwayPlayer == null)
                return "";

            var raceRules = new RaceRules(SelectedLeague, SelectedGameType);
            int homeRaceTo = raceRules.GetRaceToValueAgainstOpponent(HomePlayer.SkillLevel, AwayPlayer.SkillLevel);

            string baseValue = homeRaceTo.ToString();
            if (raceRules.IsRaceToOpponentDependent)
                return $"{baseValue}\n(vs opponent)";
            return baseValue;
        }
    }

    public string AwayPlayerRaceToDisplay
    {
        get
        {
            if (HomePlayer == null || AwayPlayer == null)
                return "";

            var raceRules = new RaceRules(SelectedLeague, SelectedGameType);
            int awayRaceTo = raceRules.GetRaceToValueAgainstOpponent(AwayPlayer.SkillLevel, HomePlayer.SkillLevel);

            string baseValue = awayRaceTo.ToString();
            if (raceRules.IsRaceToOpponentDependent)
                return $"{baseValue}\n(vs opponent)";
            return baseValue;
        }
    }

    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        // Update UI based on game state changes
        // This will be extended as we add more game tracking
    }

    public void SetupPlayers(League league, GameType gameType)
    {
        // Unsubscribe from old players if they exist
        if (HomePlayer != null)
            HomePlayer.PropertyChanged -= OnPlayerPropertyChanged;
        if (AwayPlayer != null)
            AwayPlayer.PropertyChanged -= OnPlayerPropertyChanged;

        SelectedLeague = league;
        SelectedGameType = gameType;

        HomePlayer = new PlayerViewModel(league, gameType);
        AwayPlayer = new PlayerViewModel(league, gameType);

        // Subscribe to skill level changes to update race-to displays
        HomePlayer.PropertyChanged += OnPlayerPropertyChanged;
        AwayPlayer.PropertyChanged += OnPlayerPropertyChanged;
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.SkillLevel) || e.PropertyName == nameof(PlayerViewModel.FargoRating))
        {
            OnPropertyChanged(nameof(HomePlayerRaceToDisplay));
            OnPropertyChanged(nameof(AwayPlayerRaceToDisplay));
        }
    }
}
