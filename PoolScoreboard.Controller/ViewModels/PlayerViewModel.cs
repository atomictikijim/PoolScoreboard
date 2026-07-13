using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Rules;
using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Controller.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    private readonly RaceRules _raceRules;
    private readonly Player _player;
    private readonly League _league;

    [ObservableProperty]
    private string teamName = string.Empty;

    [ObservableProperty]
    private string playerName = string.Empty;

    [ObservableProperty]
    private int skillLevel = 5;

    [ObservableProperty]
    private int fargoRating = 500;

    [ObservableProperty]
    private int raceToValue = 5;

    [ObservableProperty]
    private int gameScore = 0;

    [ObservableProperty]
    private int matchScore = 0;

    public bool UsesSkillLevel => _league == League.APA || _league == League.TAP;
    public bool UsesFargoRating => _league == League.USAPL || _league == League.BCA;

    public string RaceToDisplay
    {
        get
        {
            string baseValue = RaceToValue.ToString();
            if (_raceRules.IsRaceToOpponentDependent)
                return $"{baseValue}\n(Opponent-dependent)";
            return baseValue;
        }
    }

    public PlayerViewModel(League league, GameType gameType)
    {
        _league = league;
        _raceRules = new RaceRules(league, gameType);
        _player = new Player { Name = "", SkillLevel = 5, TeamName = "", League = league };
    }

    partial void OnSkillLevelChanged(int value)
    {
        _player.SkillLevel = value;
        RaceToValue = _raceRules.GetRaceToValue(_player);
    }

    partial void OnFargoRatingChanged(int value)
    {
        _player.SkillLevel = (value / 100);
        RaceToValue = _raceRules.GetRaceToValue(_player);
    }

    partial void OnRaceToValueChanged(int oldValue, int newValue)
    {
        OnPropertyChanged(nameof(RaceToDisplay));
    }

    partial void OnTeamNameChanged(string value)
    {
        _player.TeamName = value;
    }

    partial void OnPlayerNameChanged(string value)
    {
        _player.Name = value;
    }

    [RelayCommand]
    public void IncrementGameScore()
    {
        GameScore++;
    }

    [RelayCommand]
    public void DecrementGameScore()
    {
        if (GameScore > 0)
            GameScore--;
    }

    [RelayCommand]
    public void IncrementMatchScore()
    {
        MatchScore++;
    }

    [RelayCommand]
    public void DecrementMatchScore()
    {
        if (MatchScore > 0)
            MatchScore--;
    }

    public Player GetPlayer() => _player;
}
