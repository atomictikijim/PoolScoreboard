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

    [ObservableProperty]
    private string teamName = string.Empty;

    [ObservableProperty]
    private string playerName = string.Empty;

    [ObservableProperty]
    private int skillLevel = 5;

    [ObservableProperty]
    private int raceToValue = 5;

    [ObservableProperty]
    private int gameScore = 0;

    [ObservableProperty]
    private int matchScore = 0;

    [ObservableProperty]
    private bool isAtTable = false;

    public PlayerViewModel(League league, GameType gameType)
    {
        _raceRules = new RaceRules(league, gameType);
        _player = new Player { Name = "", SkillLevel = 5, TeamName = "", League = league };
    }

    partial void OnSkillLevelChanged(int value)
    {
        _player.SkillLevel = value;
        RaceToValue = _raceRules.GetRaceToValue(_player);
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

    [RelayCommand]
    public void ToggleAtTable()
    {
        IsAtTable = !IsAtTable;
    }

    public Player GetPlayer() => _player;
}
